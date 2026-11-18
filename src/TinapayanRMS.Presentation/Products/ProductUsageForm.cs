using System.Collections.ObjectModel;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What the section form (<see cref="ProductUsageForm"/>) asks of the Products screen, besides its runners, messages and
/// undo steps: the screen's cost basis, which its rows are priced at, and what a linked section could link instead. The
/// screen implements it explicitly.
/// </summary>
public interface IProductUsageFormHost : IScreenPartHost, IUndoRecorder
{
    /// <summary>Which of the materials' costs the screen shows, buffered or not: what a row is priced at.</summary>
    CostBasis CostBasis { get; }

    /// <summary>Every sub-assembly that could be linked or copied, as the screen last loaded them.</summary>
    IReadOnlyList<SubAssembly> Linkable { get; }

    /// <summary>The sub-assemblies the recipe's sections link or own (0 for a section not saved yet).</summary>
    IEnumerable<int> SectionSubAssemblyIds { get; }
}

/// <summary>
/// The Products screen's add/edit sub-assembly form (UsageForm, through ProductsState.UsageForm):
/// the section it edits or the one it adds, its fields and tables, and what it shows worked out from them. Its own
/// controls are its own: the Remove Line under each table, a linked section's Edit and Cancel, and the piece size an add
/// starts at. The screen keeps what the form does to the recipe (opening it, Apply, a link changed, a move), since that
/// changes the recipe's sections, its tabs and the undo history these fields are part of, and says when the lines or what
/// the form adds have changed. The form works out its own piece size and figures from its tables (<see cref="Recompute"/>).
/// </summary>
public sealed class ProductUsageForm : ObservableState, IDisposable
{
    private readonly IProductUsageFormHost _host;
    private readonly IReadOnlyList<RawMaterial> _ingredients;

    /// <summary>The form's batch weight, piece cost and captions, worked out when read (<see cref="Figures"/>).</summary>
    private readonly Derived<FormFigures> _figures;

    /// <param name="host">The Products screen: its cost basis, what can be linked, its messages and its undo steps.</param>
    /// <param name="ingredients">The ingredients as the screen last loaded them, which a row prices itself from.</param>
    public ProductUsageForm(IProductUsageFormHost host, IReadOnlyList<RawMaterial> ingredients)
    {
        _host = host;
        _ingredients = ingredients;

        // The form's figures follow any change to its tables, and to the form itself (OnPropertyChanged).
        _figures = new Derived<FormFigures>(Figures, RaiseFiguresChanged);
        RowChanges.OnAnyChange(FormLines, _figures.Invalidate);
        RowChanges.OnAnyChange(FormAddIns, _figures.Invalidate);

        // Every row put in a lines table, however it got there (a product read, an undo, a form opened), is priced at the
        // screen's costs, buffered or not; the add rows' factories see to a row still being typed.
        FormLines.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = host.CostBasis).Subscribe();
        FormAddIns.ToObservableChangeSet().OnItemAdded(r => r.CostBasis = host.CostBasis).Subscribe();

        // The add form's piece size starts as its role has it (StartingPieceSize), so it follows a role picked, or a
        // copy's taken, while it's still what the role before started it at: neither figure typed.
        RolePicks
            .Where(pick => IsUsageFormOpen && IsAddingUsage && IsPieceSizeAsStarted(pick.Before))
            .Subscribe(_ => StartPieceSize());

        StartChangingLinkCommand = ScreenCommand.Of(StartChangingLink, () => IsEditingLinkedUsage);
        CancelChangingLinkCommand = ScreenCommand.Of(() => IsChangingLink = false);
        RemoveLineCommand = ScreenCommand.Of(() => host.AsUndoStep(() => FormLines.Remove(SelectedFormLine!)), () => SelectedFormLine is not null);
        RemoveAddInCommand = ScreenCommand.Of(() => host.AsUndoStep(() => FormAddIns.Remove(SelectedFormAddIn!)), () => SelectedFormAddIn is not null);
    }

    /// <summary>The linked badge's Edit: shows the picker of what the section could link instead (IsChangingLink).</summary>
    public ScreenCommand StartChangingLinkCommand { get; }

    /// <summary>Puts the badge back without changing the link.</summary>
    public ScreenCommand CancelChangingLinkCommand { get; }

    /// <summary>Removes the line picked in the form's lines table, as one undo step.</summary>
    public ScreenCommand RemoveLineCommand { get; }

    /// <summary>Removes the add-in picked in the form's add-ins table, as one undo step.</summary>
    public ScreenCommand RemoveAddInCommand { get; }

    /// <summary>An ingredient as the screen last loaded it, for a row that prices itself.</summary>
    private RawMaterial? FindIngredient(int id) => _ingredients.FirstOrDefault(m => m.Id == id);

    /// <summary>
    /// The roles the form offers: every recipe role, as a recipe can have several sub-assemblies in one, told apart by
    /// their names. Never the Packaging role: the Packaging tab takes the product's packaging.
    /// </summary>
    public ObservableCollection<SubAssemblyRole> FormRoleOptions { get; } = [];

    /// <summary>
    /// The add form makes a new sub-assembly that Save keeps on the Sub-Assemblies screen, linked: its role isn't
    /// product-owned (SubAssemblyRole.IsProductOwned), where it would stay the product's own.
    /// The form's "Moves to Sub-Assemblies on Save" badge shows then, in the place of a title.
    /// </summary>
    public bool IsFormNewMovingToSubAssemblies =>
        IsAddingUsage && EditingUsageMode == UsageFormMode.New && EditingUsageRole is { IsProductOwned: false };

    /// <summary>
    /// Other products' sub-assemblies the form can link or copy. Linking leaves out the ones the recipe
    /// already uses; a product never links or copies its own.
    /// </summary>
    public ObservableCollection<LinkableSubAssemblyOption> LinkableSubAssemblies { get; } = [];

    /// <summary>The form's lines, for a new section: typed straight into its table.</summary>
    public ObservableCollection<IngredientLineRow> FormLines { get; } = [];

    /// <summary>The line the form table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewFormLine => () => new IngredientLineRow(FindIngredient, FormFormulaBase) { CostBasis = _host.CostBasis };

    /// <summary>
    /// The form's add-ins, for a sub-assembly the product links: its own extras on top of the owner's
    /// recipe, typed straight into their own table under it.
    /// </summary>
    public ObservableCollection<IngredientLineRow> FormAddIns { get; } = [];

    /// <summary>The add-in the form table's add row edits, priced as its ingredient is picked.</summary>
    public Func<IngredientLineRow> NewFormAddIn => () => new IngredientLineRow(FindIngredient, FormFormulaBase) { CostBasis = _host.CostBasis };

    /// <summary>The form's formula base as last applied to its rows (<see cref="Recompute"/>), for a line started in either table's add row.</summary>
    internal decimal FormFormulaBase
    {
        get;
        set
        {
            if (value != field)
            {
                field = value;
                _figures.Invalidate();
            }
        }
    }

    private ProductUsageEdit? _editingUsage;
    /// <summary>
    /// The section the form edits, or null while it adds one. A sub-assembly's tab is its form, so this is the
    /// tab's own section whenever one shows; the form's header reads its name, badges and where it's linked from.
    /// </summary>
    public ProductUsageEdit? EditingUsage
    {
        get => _editingUsage;
        internal set
        {
            RaiseAndSetIfChanged(ref _editingUsage, value);
            RaisePropertyChanged(nameof(IsEditingLinkedUsage));
            RaisePropertyChanged(nameof(IsEditingOwnedUsage));
            RaisePropertyChanged(nameof(CanMoveEditingUsageToInHouse));
            RaisePropertyChanged(nameof(IsFormLinesEditable));

            // A link being changed belongs to the section it was started on.
            IsChangingLink = false;
            RaisePropertyChanged(nameof(IsShowingLinkBadge));
        }
    }

    /// <summary>
    /// The form edits a section linked from elsewhere, so Unlink and Make a Copy have something to copy. A plain
    /// <c>EditingUsage.IsLinked</c> binding can't say this: with no section it has no value at all, and the
    /// button would fall back to showing.
    /// </summary>
    public bool IsEditingLinkedUsage => EditingUsage?.IsLinked == true;

    private bool _isChangingLink;
    /// <summary>
    /// The badge's Edit was pressed: a picker of the sub-assemblies it could link instead (ChangeLinkOptions) stands in
    /// for the badge until one is picked or the change is cancelled.
    /// </summary>
    public bool IsChangingLink
    {
        get => _isChangingLink;
        internal set
        {
            RaiseAndSetIfChanged(ref _isChangingLink, value);
            RaisePropertyChanged(nameof(IsShowingLinkBadge));
        }
    }

    /// <summary>A linked section's badge and its Edit show: the form edits one, and its link isn't being changed.</summary>
    public bool IsShowingLinkBadge => IsEditingLinkedUsage && !IsChangingLink;

    /// <summary>
    /// What a linked section can link instead: the Sub-Assemblies screen's recipes the recipe doesn't use yet, whatever
    /// their role. Packaging goes through the Packaging tab, and a product's own sub-assembly is never linked.
    /// </summary>
    public ObservableCollection<LinkableSubAssemblyOption> ChangeLinkOptions { get; } = [];

    private LinkableSubAssemblyOption? _changeLinkTarget;
    /// <summary>The sub-assembly picked to link instead (<see cref="PickLinkTarget"/>), or none while the picker opens.</summary>
    public LinkableSubAssemblyOption? ChangeLinkTarget
    {
        get => _changeLinkTarget;
        private set => RaiseAndSetIfChanged(ref _changeLinkTarget, value);
    }

    /// <summary>The user picks the sub-assembly to link instead: the section relinks to it (the screen's ChangeLinkAsync).</summary>
    public void PickLinkTarget(LinkableSubAssemblyOption? target)
    {
        if (target != ChangeLinkTarget)
        {
            ChangeLinkTarget = target;
            _linkTargetPicks.OnNext(target);
        }
    }

    // Each pick the form's pickers make, or the screen makes for the user (opening a form, Open from Sub-Assemblies):
    // what the screen reacts to. Fed by the Pick methods alone (PickMode, PickRole, PickSource, PickLinkTarget): an undo
    // putting the form back, or the option lists being rebuilt, writes the properties, which shows them without anything
    // reacting.
    private readonly Subject<UsageFormMode> _modePicks = new();
    private readonly Subject<(SubAssemblyRole? Before, SubAssemblyRole? After)> _rolePicks = new();
    private readonly Subject<LinkableSubAssemblyOption?> _sourcePicks = new();
    private readonly Subject<LinkableSubAssemblyOption?> _linkTargetPicks = new();

    /// <summary>Each way picked for the add form to take its sub-assembly: the form starts again empty in it (the screen's StartUsageFormMode).</summary>
    public IObservable<UsageFormMode> ModePicks => _modePicks;

    /// <summary>Each role picked, with the one before it: the piece size follows while it's still the one the role before started it at.</summary>
    public IObservable<(SubAssemblyRole? Before, SubAssemblyRole? After)> RolePicks => _rolePicks;

    /// <summary>Each sub-assembly or packaging picked to link or copy: its lines load into the form (the screen's LoadUsageSourceAsync).</summary>
    public IObservable<LinkableSubAssemblyOption?> SourcePicks => _sourcePicks;

    /// <summary>Each sub-assembly picked to link instead of the one linked: the section relinks to it (the screen's ChangeLinkAsync).</summary>
    public IObservable<LinkableSubAssemblyOption?> LinkTargetPicks => _linkTargetPicks;

    /// <summary>
    /// The form edits one of the product's own that isn't already on its way to the Sub-Assemblies screen, so Move to
    /// Sub-Assemblies has something to move. Its own property for the same reason as <see cref="IsEditingLinkedUsage"/>.
    /// </summary>
    public bool IsEditingOwnedUsage => EditingUsage is { IsOwned: true, IsReleasing: false, IsMovingToInHouse: false };

    /// <summary>The form edits one of the product's own that Move to In-House can move (ProductUsageEdit.CanMoveToInHouse).</summary>
    public bool CanMoveEditingUsageToInHouse => EditingUsage is { CanMoveToInHouse: true };

    private bool _isUsageFormOpen;
    public bool IsUsageFormOpen
    {
        get => _isUsageFormOpen;
        internal set => RaiseAndSetIfChanged(ref _isUsageFormOpen, value);
    }

    private string _usageFormTitle = string.Empty;
    public string UsageFormTitle
    {
        get => _usageFormTitle;
        internal set => RaiseAndSetIfChanged(ref _usageFormTitle, value);
    }

    private bool _isAddingUsage = true;
    /// <summary>Adding, not editing: only then can the form's sub-assembly be an existing one, linked or copied.</summary>
    public bool IsAddingUsage
    {
        get => _isAddingUsage;
        internal set => RaiseAndSetIfChanged(ref _isAddingUsage, value);
    }

    private UsageFormMode _editingUsageMode;
    /// <summary>Which way the add form takes its sub-assembly: what Add Sub-Assembly's drop-down picked (<see cref="PickMode"/>).</summary>
    public UsageFormMode EditingUsageMode
    {
        get => _editingUsageMode;
        internal set => RaiseAndSetIfChanged(ref _editingUsageMode, value);
    }

    /// <summary>The way in picked for the add form: it starts again empty in it (<see cref="ModePicks"/>).</summary>
    public void PickMode(UsageFormMode mode)
    {
        if (mode != EditingUsageMode)
        {
            EditingUsageMode = mode;
            _modePicks.OnNext(mode);
        }
    }

    private bool _isFormEditingLines = true;
    /// <summary>The form edits a new section: its name and role show, and its lines are typed into.</summary>
    public bool IsFormEditingLines
    {
        get => _isFormEditingLines;
        internal set
        {
            RaiseAndSetIfChanged(ref _isFormEditingLines, value);
            RaisePropertyChanged(nameof(IsFormLinesEditable));
            RaisePropertyChanged(nameof(IsFormShowingLines));
            RaisePropertyChanged(nameof(IsFormShowingAddIns));
            RaisePropertyChanged(nameof(HasFormAddIns));
            RaisePropertyChanged(nameof(IsFormShowingNoAddIns));
        }
    }

    /// <summary>
    /// The form's lines table shows: the product's own lines, to type into, or a linked one's, read-only. A link
    /// has none to show until the sub-assembly it comes from has been picked and its lines loaded.
    /// </summary>
    public bool IsFormShowingLines => IsFormEditingLines || FormLines.Count > 0;

    /// <summary>
    /// The form's lines can be typed into: a new or own section's, and a linked one's too, which Save writes to the
    /// sub-assembly where it's kept, for every product linking it. Only a link being picked shows them read-only.
    /// </summary>
    public bool IsFormLinesEditable => IsFormEditingLines || IsEditingLinkedUsage;

    /// <summary>
    /// The form's add-ins table shows, always, empty or not: only for a sub-assembly the product links, and only
    /// once the recipe it sits on top of is there to sit on. A sub-assembly of the product's own has no add-ins —
    /// its extras just go in its lines. A shut table would hide that the batch
    /// has extras in it, so the table stays open and the badge above the lines says
    /// whether it has any (FormAddInsBadgeText).
    /// </summary>
    public bool IsFormShowingAddIns => !IsFormEditingLines && IsFormShowingLines;

    /// <summary>The add-ins table's lines with an ingredient picked: the add row, or a line not yet picked, isn't one.</summary>
    public int FormAddInCount => FormAddIns.Count(l => l.IngredientId is not null);

    /// <summary>The add-ins badge beside a linked recipe's badge is a link: it has add-ins, and a click scrolls down to them.</summary>
    public bool HasFormAddIns => IsFormShowingAddIns && FormAddInCount > 0;

    /// <summary>The add-ins badge is a plain grey tag: a linked recipe with nothing added to it.</summary>
    public bool IsFormShowingNoAddIns => IsFormShowingAddIns && FormAddInCount == 0;

    /// <summary>The badge's text: "No add-ins", "1 add-in" or "3 add-ins".</summary>
    public string FormAddInsBadgeText => FormAddInCount switch
    {
        0 => "No add-ins",
        1 => "1 add-in",
        var count => $"{count} add-ins",
    };

    private bool _isFormPickingSource;
    /// <summary>The form links or copies an existing sub-assembly (another product's, or a standalone one): its picker shows.</summary>
    public bool IsFormPickingSource
    {
        get => _isFormPickingSource;
        internal set => RaiseAndSetIfChanged(ref _isFormPickingSource, value);
    }

    private SubAssemblyRole? _editingUsageRole;
    /// <summary>The role of the new section the form edits, which is what it's called. Picked from FormRoleOptions (<see cref="PickRole"/>).</summary>
    public SubAssemblyRole? EditingUsageRole
    {
        get => _editingUsageRole;
        internal set => RaiseAndSetIfChanged(ref _editingUsageRole, value);
    }

    /// <summary>A role picked for the section: the piece size may follow it (<see cref="RolePicks"/>).</summary>
    public void PickRole(SubAssemblyRole? role)
    {
        var before = EditingUsageRole;
        if (role != before)
        {
            EditingUsageRole = role;
            _rolePicks.OnNext((before, role));
        }
    }

    private string? _editingUsageName;
    /// <summary>
    /// The optional name of the new section the form edits: what tells it apart from others in its role,
    /// its tab reading "Filling: Ube" beside "Filling: Cheese".
    /// </summary>
    public string? EditingUsageName
    {
        get => _editingUsageName;
        set => RaiseAndSetIfChanged(ref _editingUsageName, value);
    }

    private LinkableSubAssemblyOption? _editingUsageSource;
    /// <summary>The existing sub-assembly to link or copy (<see cref="PickSource"/>).</summary>
    public LinkableSubAssemblyOption? EditingUsageSource
    {
        get => _editingUsageSource;
        internal set => RaiseAndSetIfChanged(ref _editingUsageSource, value);
    }

    /// <summary>A sub-assembly or packaging picked to link or copy: its lines load into the form (<see cref="SourcePicks"/>).</summary>
    public void PickSource(LinkableSubAssemblyOption? source)
    {
        if (source != EditingUsageSource)
        {
            EditingUsageSource = source;
            _sourcePicks.OnNext(source);
        }
    }

    /// <summary>
    /// The form's kind changed: adding or editing, and an add's way in, role or source, however each was set (a pick, an
    /// undo, the form opened on a section). The preview, and whether Save moves a new section to the Sub-Assemblies screen,
    /// follow it (the screen's ProductRecipe.FormKindChanged).
    /// </summary>
    public event Action? KindChanged;

    protected override void OnPropertyChanged(string? propertyName)
    {
        if (propertyName is nameof(IsAddingUsage) or nameof(EditingUsageMode) or nameof(EditingUsageRole) or nameof(EditingUsageSource))
        {
            KindChanged?.Invoke();
        }

        // Broadly: the figures read the piece size, the section edited and the source picked, and anything else of the
        // form's is only one more computation when they're next read.
        _figures.Invalidate();
    }

    private IngredientLineRow? _selectedFormLine;
    public IngredientLineRow? SelectedFormLine
    {
        get => _selectedFormLine;
        set => RaiseAndSetIfChanged(ref _selectedFormLine, value);
    }

    private IngredientLineRow? _selectedFormAddIn;
    public IngredientLineRow? SelectedFormAddIn
    {
        get => _selectedFormAddIn;
        set => RaiseAndSetIfChanged(ref _selectedFormAddIn, value);
    }

    // The piece size: the user types either figure into its box (TypeWeightPerPiece, TypeYield), and the other follows from
    // the batch weight. Only the one typed is kept (AuthoredField). The screen shows a size (ShowPieceSize) or puts one back
    // (RestorePieceSize).

    /// <summary>Which piece-size figure was typed, and so stays put when the batch changes.</summary>
    internal UsageAuthoredField AuthoredField { get; private set; } = UsageAuthoredField.WeightPerPiece;

    /// <summary>The figure typed: <see cref="AuthoredField"/>'s.</summary>
    internal decimal AuthoredValue => AuthoredField == UsageAuthoredField.WeightPerPiece ? _editingUsageWeightPerPiece : _editingUsageYield;

    private decimal _editingUsageWeightPerPiece;
    /// <summary>Grams of the batch in a piece. Typed (<see cref="TypeWeightPerPiece"/>), it's the figure kept, and the yield follows.</summary>
    public decimal EditingUsageWeightPerPiece => _editingUsageWeightPerPiece;

    private decimal _editingUsageYield;
    /// <summary>Pieces a batch makes. Typed (<see cref="TypeYield"/>), it's the figure kept, and the weight per piece follows.</summary>
    public decimal EditingUsageYield => _editingUsageYield;

    /// <summary>The user types the weight per piece: it's the figure kept, and the yield follows.</summary>
    public void TypeWeightPerPiece(decimal value) => PieceSizeTyped(UsageAuthoredField.WeightPerPiece, value);

    /// <summary>The user types the yield: it's the figure kept, and the weight per piece follows.</summary>
    public void TypeYield(decimal value) => PieceSizeTyped(UsageAuthoredField.Yield, value);

    /// <summary>
    /// Shows a piece size, the other figure worked out from the batch, and the form's figures with it: a section opened, or
    /// the size a role starts at.
    /// </summary>
    internal void ShowPieceSize(UsageAuthoredField authoredField, decimal authoredValue)
    {
        AuthoredField = authoredField;
        SetPieceSizeFigure(authoredField, authoredValue);
        FollowPieceSize();
    }

    /// <summary>What the piece size is worked out from changed (a line, an add-in): the figure not typed follows, and the form's figures with it.</summary>
    internal void FollowPieceSize()
    {
        var (weightPerPiece, yield) = ProductRules.ResolvePieceSize(AuthoredField, AuthoredValue, Batch().Sum(l => l.Grams));

        // Rounded to the 2 decimals the box shows.
        if (AuthoredField == UsageAuthoredField.WeightPerPiece)
        {
            SetPieceSizeFigure(UsageAuthoredField.Yield, Math.Round(yield, 2));
        }
        else
        {
            SetPieceSizeFigure(UsageAuthoredField.WeightPerPiece, Math.Round(weightPerPiece, 2));
        }

        Recompute();
    }

    private void PieceSizeTyped(UsageAuthoredField authoredField, decimal value)
    {
        if (value != (authoredField == UsageAuthoredField.WeightPerPiece ? _editingUsageWeightPerPiece : _editingUsageYield))
        {
            ShowPieceSize(authoredField, value);
        }
    }

    private void SetPieceSizeFigure(UsageAuthoredField field, decimal value)
    {
        if (field == UsageAuthoredField.WeightPerPiece)
        {
            RaiseAndSetIfChanged(ref _editingUsageWeightPerPiece, value, nameof(EditingUsageWeightPerPiece));
        }
        else
        {
            RaiseAndSetIfChanged(ref _editingUsageYield, value, nameof(EditingUsageYield));
        }
    }

    /// <summary>
    /// The form's batch, as each line's grams and cost per gram (buffered, unless the screen isn't): both its tables, the
    /// recipe and this product's add-ins on top of it, leaving out any row with no ingredient picked yet. Everything the form
    /// works out from the batch reads it here, so add-ins reach all of it at once.
    /// </summary>
    internal IReadOnlyList<(decimal Grams, decimal CostPerGram)> Batch() =>
        [.. FormLines.Concat(FormAddIns).Where(l => l.Ingredient is not null).Select(l => (l.Grams ?? 0m, l.CostPerGram))];

    /// <summary>The batch that goes in, e.g. "1,700 g".</summary>
    public string UsageFormTotalWeightText => _figures.Value.TotalWeightText;

    /// <summary>Which figure stays put when the batch changes, e.g. "Weight per piece stays at 60 g if the batch changes".</summary>
    public string UsageFormPieceSizeCaption => _figures.Value.PieceSizeCaption;

    /// <summary>
    /// The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by — or,
    /// with nothing ticked, what the absolute percentages are read against.
    /// </summary>
    public string UsageFormBaseCaption => _figures.Value.BaseCaption;

    /// <summary>Both form tables' percentage header: "Baker's %" while the recipe has a base, "Absolute %" while it has none.</summary>
    public string UsageFormPercentHeader => IngredientLineRow.PercentHeader(FormFormulaBase);

    /// <summary>The batch the two tables come to, e.g. "1,700 g from Sweet Dough + 20 g of add-ins = a 1,720 g batch."</summary>
    public string UsageFormAddInsCaption => _figures.Value.AddInsCaption;

    /// <summary>
    /// What a piece of the form's sub-assembly or packaging costs: the piece-size column's first tile, to two decimals
    /// ("0.50"), and the packaging table's footer, to four ("0.5000").
    /// </summary>
    public string UsageFormCostPerPieceText => _figures.Value.CostPerPieceText;

    /// <summary>What the form's whole batch costs, its lines and add-ins together, e.g. "73.98": the piece-size column's second tile.</summary>
    public string UsageFormBatchCostText => _figures.Value.BatchCostText;

    /// <summary>
    /// The formula's base, given to both tables: an add-in is read against the recipe's base, never its own, and with no
    /// base every line of both is read against the whole batch, add-ins included, so they come to 100%. A write into the
    /// rows (each one's percentage, and the grams of a line typed as a percentage), so it's pushed, where the figures that
    /// read the form are worked out as they're read.
    /// </summary>
    internal void Recompute()
    {
        var (totalWeight, _) = SubAssemblyRules.CalculateTotals(Batch());
        var formulaBase = IngredientLineRow.ApplyFormulaBase([.. FormLines], totalWeight);
        FormFormulaBase = formulaBase;
        IngredientLineRow.ApplyFormulaBase(FormAddIns, formulaBase, totalWeight);
    }

    /// <summary>The form's figures, worked out together from the form as it stands.</summary>
    private sealed record FormFigures(
        string TotalWeightText, string PieceSizeCaption, string BaseCaption, string AddInsCaption, string CostPerPieceText, string BatchCostText);

    /// <summary>The form's batch weight, which figure stays put, what a piece costs, and the tables' captions.</summary>
    private FormFigures Figures()
    {
        var batch = Batch();
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(batch);
        var authoredValue = AuthoredValue;
        var (weightPerPiece, _) = ProductRules.ResolvePieceSize(AuthoredField, authoredValue, totalWeight);

        // Neither figure is needed to put the section in (ProductRules.EnsureValidUsages), so with none typed the
        // caption says what that costs: nothing per piece, until one is. A yield of 1 is what a section in a
        // product-owned role starts at (StartingPieceSize), so it says how to size a bigger batch.
        var pieceSizeCaption = authoredValue <= 0
            ? "Type the weight per piece or the yield; the other is worked out. Until then this section costs nothing per piece."
            : AuthoredField == UsageAuthoredField.Yield && authoredValue == 1
                ? "One piece takes the whole batch, and still does if the batch changes. For a batch that makes more, type the weight per piece or the yield."
            : AuthoredField == UsageAuthoredField.WeightPerPiece
                ? $"Weight per piece stays at {authoredValue:#,0.##} g if the batch changes."
                : $"The yield stays at {authoredValue:#,0.##} pieces if the batch changes.";

        // No pieces yet shows as a dash rather than 0.00, which would read as a section that costs nothing.
        var costPerPiece = ProductRules.HasPieces(totalWeight, weightPerPiece)
            ? ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, totalWeight)
            : (decimal?)null;

        var formulaBase = FormFormulaBase;
        var baseCaption = formulaBase > 0
            ? $"Base {formulaBase:#,0.##} g · whole formula {totalWeight / formulaBase * 100m:#,0.#}%"
            : SubAssemblyBatch.NoBaseCaption;

        // The arithmetic of the two tables, in words: the recipe's grams, this product's on top, the batch.
        var recipeName = EditingUsage?.DisplayName
            ?? EditingUsageSource?.SubAssembly?.Name ?? "the recipe";
        var addInWeight = FormAddIns.Where(l => l.Ingredient is not null).Sum(l => l.Grams);
        var baseWeight = totalWeight - addInWeight;
        var addInsCaption = addInWeight > 0
            ? $"{baseWeight:#,0.##} g from {recipeName} + {addInWeight:#,0.##} g of add-ins = a {totalWeight:#,0.##} g batch."
            : $"Nothing extra yet — the batch is {recipeName}'s {baseWeight:#,0.##} g.";

        return new FormFigures(
            $"{totalWeight:#,0.##} g",
            pieceSizeCaption,
            baseCaption,
            addInsCaption,
            // Two decimals, as Batch cost beside it; packaging's, above, keeps four.
            costPerPiece is { } figure ? $"{figure:N2}" : "—",
            batch.Count > 0 ? $"{totalCost:N2}" : "—");
    }

    private void RaiseFiguresChanged()
    {
        RaisePropertyChanged(nameof(UsageFormTotalWeightText));
        RaisePropertyChanged(nameof(UsageFormPieceSizeCaption));
        RaisePropertyChanged(nameof(UsageFormBaseCaption));
        RaisePropertyChanged(nameof(UsageFormPercentHeader));
        RaisePropertyChanged(nameof(UsageFormAddInsCaption));
        RaisePropertyChanged(nameof(UsageFormCostPerPieceText));
        RaisePropertyChanged(nameof(UsageFormBatchCostText));
    }

    /// <summary>Puts the piece size back as it stood, for an undo: which figure was typed, and both figures.</summary>
    internal void RestorePieceSize(UsageAuthoredField authoredField, decimal weightPerPiece, decimal yield)
    {
        AuthoredField = authoredField;
        _editingUsageWeightPerPiece = weightPerPiece;
        RaisePropertyChanged(nameof(EditingUsageWeightPerPiece));
        _editingUsageYield = yield;
        RaisePropertyChanged(nameof(EditingUsageYield));
    }

    /// <summary>The form's mode, role or source changed: whether Save moves a new one to the Sub-Assemblies screen follows.</summary>
    internal void RaiseKindChanged() => RaisePropertyChanged(nameof(IsFormNewMovingToSubAssemblies));

    /// <summary>The form's lines or add-ins changed: what its tables and the add-ins badge show follows.</summary>
    internal void RaiseLinesChanged()
    {
        RaisePropertyChanged(nameof(IsFormShowingLines));
        RaisePropertyChanged(nameof(IsFormShowingAddIns));
        RaisePropertyChanged(nameof(FormAddInCount));
        RaisePropertyChanged(nameof(HasFormAddIns));
        RaisePropertyChanged(nameof(IsFormShowingNoAddIns));
        RaisePropertyChanged(nameof(FormAddInsBadgeText));
    }

    /// <summary>The badge's Edit: the picker, holding what the section could link instead, in the badge's place.</summary>
    private void StartChangingLink()
    {
        var usedIds = _host.SectionSubAssemblyIds.ToHashSet();
        ChangeLinkOptions.MergeFrom(
            [.. _host.Linkable
                .Where(s => s.OwnerProductId is null && s.Role?.IsPackaging != true && !usedIds.Contains(s.Id))
                .Select(s => new LinkableSubAssemblyOption(s))],
            o => o.Key);

        if (ChangeLinkOptions.Count == 0)
        {
            _host.ShowError("There's no other sub-assembly to link: every one on the Sub-Assemblies screen is in this recipe already.");
            return;
        }

        _host.ShowError(null);
        ChangeLinkTarget = null;
        IsChangingLink = true;
    }

    /// <summary>
    /// The piece size the add form starts at in <paramref name="role"/>. A section made in a product-owned role
    /// (SubAssemblyRole.IsProductOwned), new or copied, is written for the one piece it goes into, so it starts at a
    /// yield of 1: its weight per piece is the whole batch, and follows the lines as they're typed. Any other starts
    /// with no size. So does a link, whose batch is the Sub-Assemblies screen's and shared.
    /// </summary>
    private (UsageAuthoredField Field, decimal Value) StartingPieceSize(SubAssemblyRole? role) =>
        EditingUsageMode != UsageFormMode.Link && role is { IsProductOwned: true }
            ? (UsageAuthoredField.Yield, 1m)
            : (UsageAuthoredField.WeightPerPiece, 0m);

    /// <summary>Shows the piece size the add form's role starts at.</summary>
    internal void StartPieceSize()
    {
        var (field, value) = StartingPieceSize(EditingUsageRole);
        ShowPieceSize(field, value);
    }

    /// <summary>
    /// Whether the add form's piece size is still the one <paramref name="role"/> starts it at, with neither figure
    /// typed. Worked out from the form, not remembered, so it holds for a form an undo put back. No size is no size,
    /// whichever box it was left in.
    /// </summary>
    internal bool IsPieceSizeAsStarted(SubAssemblyRole? role)
    {
        var (field, value) = StartingPieceSize(role);
        return AuthoredValue == value && (value == 0 || AuthoredField == field);
    }

    /// <summary>Puts <paramref name="lines"/> in the form's lines table, in place of whatever it held.</summary>
    internal void ShowLines(IEnumerable<SectionLine> lines)
    {
        FormLines.Clear();
        FormLines.AddRange(lines.Select(l => new IngredientLineRow(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
    }

    /// <summary>The same for the add-ins table under it.</summary>
    internal void ShowAddIns(IEnumerable<SectionLine> addIns)
    {
        FormAddIns.Clear();
        FormAddIns.AddRange(addIns.Select(a => new IngredientLineRow(a.Ingredient, a.Grams)));
    }

    /// <summary>Whether the lines table still shows <paramref name="lines"/>, with nothing typed into it since.</summary>
    internal bool IsShowing(IReadOnlyList<SectionLine> lines) =>
        FormLines.Count == lines.Count
        && FormLines.Zip(lines).All(pair =>
            pair.First.IngredientId == pair.Second.Ingredient.Id
            && pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent);

    /// <summary>
    /// The lines and add-ins at their ingredients' refreshed costs, as the screen last loaded them. It reprices in place
    /// rather than refilling the tables, so a refresh arriving while an add-in is being typed doesn't throw it away.
    /// </summary>
    internal void ResolveRows()
    {
        var ingredientsById = _ingredients.ToDictionary(m => m.Id);
        IngredientLineRow.Resolve(FormLines, ingredientsById);
        IngredientLineRow.Resolve(FormAddIns, ingredientsById);
    }

    /// <summary>Every row priced at the screen's cost basis again: Unbuffered or Buffered was pressed.</summary>
    internal void Reprice()
    {
        foreach (var row in FormLines.Concat(FormAddIns))
        {
            row.CostBasis = _host.CostBasis;
        }
    }

    public void Dispose()
    {
        _modePicks.Dispose();
        _rolePicks.Dispose();
        _sourcePicks.Dispose();
        _linkTargetPicks.Dispose();
    }
}
