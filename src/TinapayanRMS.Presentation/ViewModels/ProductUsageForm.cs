using System.Collections.ObjectModel;
using System.Reactive.Subjects;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The Products screen's add/edit sub-assembly form (UsageFormPanel, through ProductsViewModel.UsageForm):
/// the section it edits or the one it adds, its fields and tables, and what it shows worked out from them.
/// The screen keeps what the form does to the recipe (opening it, Apply, a link changed, a move), since that changes the
/// recipe's sections, its tabs and the undo history these fields are part of, and says when the lines or what the form
/// adds have changed. The form works out its own piece size and figures from its tables (<see cref="Recompute"/>).
/// </summary>
/// <param name="findIngredient">An ingredient as the screen last loaded it, for a row that prices itself.</param>
/// <param name="findPackagingMaterial">A packaging material as the screen last loaded it.</param>
/// <param name="costBasis">The screen's cost basis, buffered or not, which a new row is priced at.</param>
public sealed class ProductUsageForm(
    Func<int, RawMaterial?> findIngredient, Func<int, RawMaterial?> findPackagingMaterial, Func<CostBasis> costBasis) : ReactiveObject, IDisposable
{
    /// <summary>
    /// The roles the form offers: every recipe role, as a recipe can have several sub-assemblies in one, told apart by
    /// their names; and the Packaging role while there's no packaging.
    /// </summary>
    public ObservableCollection<SubAssemblyRole> FormRoleOptions { get; } = [];

    /// <summary>
    /// The add form's packaging lines, when it adds packaging (IsFormPackaging): how much of each material one piece
    /// takes. Typed into for a new or copied packaging; read-only for a link, whose lines are kept where it's from.
    /// </summary>
    public ObservableCollection<PackagingLineRow> FormPackagingLines { get; } = [];

    /// <summary>The line the form's packaging table's add row edits, priced as its material is picked.</summary>
    public Func<PackagingLineRow> NewFormPackagingLine => () => new PackagingLineRow(findPackagingMaterial) { CostBasis = costBasis() };

    private PackagingLineRow? _selectedFormPackagingLine;
    public PackagingLineRow? SelectedFormPackagingLine
    {
        get => _selectedFormPackagingLine;
        set => this.RaiseAndSetIfChanged(ref _selectedFormPackagingLine, value);
    }

    /// <summary>
    /// The add form adds the product's packaging rather than a recipe section: the Packaging role is picked for a new one,
    /// or a packaging source for a link or a copy. It then shows the packaging table in place of the ingredients, add-ins
    /// and piece size, and Apply fills the Packaging tab.
    /// </summary>
    public bool IsFormPackaging => IsAddingUsage && EditingUsageMode switch
    {
        UsageFormMode.Link => EditingUsageSource?.IsPackaging == true,
        UsageFormMode.Copy => EditingUsageSource is { } source ? source.IsPackaging : EditingUsageRole?.IsPackaging == true,
        _ => EditingUsageRole?.IsPackaging == true,
    };

    /// <summary>The form's packaging table can be typed into: a new or copied packaging, not a link.</summary>
    public bool IsFormEditingPackaging => IsFormPackaging && EditingUsageMode != UsageFormMode.Link;

    /// <summary>
    /// The add form makes a new sub-assembly that Save keeps on the Sub-Assemblies screen, linked: its role isn't
    /// product-owned (SubAssemblyRole.IsProductOwned), where it would stay the product's own, and it isn't packaging.
    /// The form's "Moves to Sub-Assemblies on Save" badge shows then, in the place of a title.
    /// </summary>
    public bool IsFormNewMovingToSubAssemblies =>
        IsAddingUsage && EditingUsageMode == UsageFormMode.New && EditingUsageRole is { IsPackaging: false, IsProductOwned: false };

    /// <summary>
    /// Other products' sub-assemblies the form can link or copy. Linking leaves out the ones the recipe
    /// already uses; a product never links or copies its own.
    /// </summary>
    public ObservableCollection<LinkableSubAssemblyOption> LinkableSubAssemblies { get; } = [];

    /// <summary>The form's lines, for a new section: typed straight into its table.</summary>
    public ObservableCollection<IngredientLineRow> FormLines { get; } = [];

    /// <summary>The line the form table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewFormLine => () => new IngredientLineRow(findIngredient, FormFormulaBase) { CostBasis = costBasis() };

    /// <summary>
    /// The form's add-ins, for a sub-assembly the product links: its own extras on top of the owner's
    /// recipe, typed straight into their own table under it.
    /// </summary>
    public ObservableCollection<IngredientLineRow> FormAddIns { get; } = [];

    /// <summary>The add-in the form table's add row edits, priced as its ingredient is picked.</summary>
    public Func<IngredientLineRow> NewFormAddIn => () => new IngredientLineRow(findIngredient, FormFormulaBase) { CostBasis = costBasis() };

    /// <summary>The form's formula base as last worked out, for a line started in either table's add row.</summary>
    internal decimal FormFormulaBase { get; set; }

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
            this.RaiseAndSetIfChanged(ref _editingUsage, value);
            this.RaisePropertyChanged(nameof(IsEditingLinkedUsage));
            this.RaisePropertyChanged(nameof(IsEditingOwnedUsage));
            this.RaisePropertyChanged(nameof(CanMoveEditingUsageToInHouse));
            this.RaisePropertyChanged(nameof(IsFormLinesEditable));

            // A link being changed belongs to the section it was started on.
            IsChangingLink = false;
            this.RaisePropertyChanged(nameof(IsShowingLinkBadge));
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
            this.RaiseAndSetIfChanged(ref _isChangingLink, value);
            this.RaisePropertyChanged(nameof(IsShowingLinkBadge));
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
    /// <summary>The sub-assembly picked to link instead: picking it relinks the section (the screen's ChangeLinkAsync).</summary>
    public LinkableSubAssemblyOption? ChangeLinkTarget
    {
        get => _changeLinkTarget;
        set
        {
            if (!EqualityComparer<LinkableSubAssemblyOption?>.Default.Equals(_changeLinkTarget, value))
            {
                this.RaiseAndSetIfChanged(ref _changeLinkTarget, value);
                _linkTargetPicks.OnNext(value);
            }
        }
    }

    // Each pick the form's pickers make, or the screen makes for the user (opening a form, Open from Sub-Assemblies):
    // what the screen reacts to. Fed by the setters alone, so an undo putting the form back (RestoreMode, RestorePicks)
    // and the option lists being rebuilt (ShowRole, ShowSource) change what shows without anything reacting.
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
        internal set => this.RaiseAndSetIfChanged(ref _isUsageFormOpen, value);
    }

    private string _usageFormTitle = string.Empty;
    public string UsageFormTitle
    {
        get => _usageFormTitle;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormTitle, value);
    }

    private bool _isAddingUsage = true;
    /// <summary>Adding, not editing: only then can the form's sub-assembly be an existing one, linked or copied.</summary>
    public bool IsAddingUsage
    {
        get => _isAddingUsage;
        internal set => this.RaiseAndSetIfChanged(ref _isAddingUsage, value);
    }

    private UsageFormMode _editingUsageMode;
    /// <summary>Which way the add form takes its sub-assembly: what Add Sub-Assembly's drop-down picked.</summary>
    public UsageFormMode EditingUsageMode
    {
        get => _editingUsageMode;
        set
        {
            if (_editingUsageMode != value)
            {
                this.RaiseAndSetIfChanged(ref _editingUsageMode, value);
                _modePicks.OnNext(value);
            }
        }
    }

    private bool _isFormEditingLines = true;
    /// <summary>The form edits a new section: its name and role show, and its lines are typed into.</summary>
    public bool IsFormEditingLines
    {
        get => _isFormEditingLines;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _isFormEditingLines, value);
            this.RaisePropertyChanged(nameof(IsFormLinesEditable));
            this.RaisePropertyChanged(nameof(IsFormShowingLines));
            this.RaisePropertyChanged(nameof(IsFormShowingAddIns));
            this.RaisePropertyChanged(nameof(HasFormAddIns));
            this.RaisePropertyChanged(nameof(IsFormShowingNoAddIns));
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
        internal set => this.RaiseAndSetIfChanged(ref _isFormPickingSource, value);
    }

    private SubAssemblyRole? _editingUsageRole;
    /// <summary>The role of the new section the form edits, which is what it's called. Picked from FormRoleOptions.</summary>
    public SubAssemblyRole? EditingUsageRole
    {
        get => _editingUsageRole;
        set
        {
            var before = _editingUsageRole;
            if (!EqualityComparer<SubAssemblyRole?>.Default.Equals(before, value))
            {
                this.RaiseAndSetIfChanged(ref _editingUsageRole, value);
                _rolePicks.OnNext((before, value));
            }
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
        set => this.RaiseAndSetIfChanged(ref _editingUsageName, value);
    }

    private LinkableSubAssemblyOption? _editingUsageSource;
    /// <summary>The existing sub-assembly to link or copy.</summary>
    public LinkableSubAssemblyOption? EditingUsageSource
    {
        get => _editingUsageSource;
        set
        {
            if (!EqualityComparer<LinkableSubAssemblyOption?>.Default.Equals(_editingUsageSource, value))
            {
                this.RaiseAndSetIfChanged(ref _editingUsageSource, value);
                _sourcePicks.OnNext(value);
            }
        }
    }

    private IngredientLineRow? _selectedFormLine;
    public IngredientLineRow? SelectedFormLine
    {
        get => _selectedFormLine;
        set => this.RaiseAndSetIfChanged(ref _selectedFormLine, value);
    }

    private IngredientLineRow? _selectedFormAddIn;
    public IngredientLineRow? SelectedFormAddIn
    {
        get => _selectedFormAddIn;
        set => this.RaiseAndSetIfChanged(ref _selectedFormAddIn, value);
    }

    // The piece size: the user types either figure into its box, and the other follows from the batch weight. Only the one
    // typed is kept (AuthoredField). A box's setter is the typing; the screen shows a size (ShowPieceSize) or puts one back
    // (RestorePieceSize) through methods of their own, so nothing has to tell the two apart.

    /// <summary>Which piece-size figure was typed, and so stays put when the batch changes.</summary>
    internal UsageAuthoredField AuthoredField { get; private set; } = UsageAuthoredField.WeightPerPiece;

    /// <summary>The figure typed: <see cref="AuthoredField"/>'s.</summary>
    internal decimal AuthoredValue => AuthoredField == UsageAuthoredField.WeightPerPiece ? _editingUsageWeightPerPiece : _editingUsageYield;

    private decimal _editingUsageWeightPerPiece;
    /// <summary>Grams of the batch in a piece. Typed, it's the figure kept, and the yield follows.</summary>
    public decimal EditingUsageWeightPerPiece
    {
        get => _editingUsageWeightPerPiece;
        set => PieceSizeTyped(UsageAuthoredField.WeightPerPiece, value);
    }

    private decimal _editingUsageYield;
    /// <summary>Pieces a batch makes. Typed, it's the figure kept, and the weight per piece follows.</summary>
    public decimal EditingUsageYield
    {
        get => _editingUsageYield;
        set => PieceSizeTyped(UsageAuthoredField.Yield, value);
    }

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
            this.RaiseAndSetIfChanged(ref _editingUsageWeightPerPiece, value, nameof(EditingUsageWeightPerPiece));
        }
        else
        {
            this.RaiseAndSetIfChanged(ref _editingUsageYield, value, nameof(EditingUsageYield));
        }
    }

    /// <summary>
    /// The form's batch, as each line's grams and cost per gram (buffered, unless the screen isn't): both its tables, the
    /// recipe and this product's add-ins on top of it, leaving out any row with no ingredient picked yet. Everything the form
    /// works out from the batch reads it here, so add-ins reach all of it at once.
    /// </summary>
    internal IReadOnlyList<(decimal Grams, decimal CostPerGram)> Batch() =>
        [.. FormLines.Concat(FormAddIns).Where(l => l.Ingredient is not null).Select(l => (l.Grams ?? 0m, l.CostPerGram))];

    private string _usageFormTotalWeightText = "0 g";
    /// <summary>The batch that goes in, e.g. "1,700 g".</summary>
    public string UsageFormTotalWeightText
    {
        get => _usageFormTotalWeightText;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormTotalWeightText, value);
    }

    private string _usageFormPieceSizeCaption = string.Empty;
    /// <summary>Which figure stays put when the batch changes, e.g. "Weight per piece stays at 60 g if the batch changes".</summary>
    public string UsageFormPieceSizeCaption
    {
        get => _usageFormPieceSizeCaption;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormPieceSizeCaption, value);
    }

    private string _usageFormBaseCaption = string.Empty;
    /// <summary>
    /// The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by — or,
    /// with nothing ticked, what the absolute percentages are read against.
    /// </summary>
    public string UsageFormBaseCaption
    {
        get => _usageFormBaseCaption;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormBaseCaption, value);
    }

    private string _usageFormPercentHeader = IngredientLineRow.PercentHeader(0m);
    /// <summary>Both form tables' percentage header: "Baker's %" while the recipe has a base, "Absolute %" while it has none.</summary>
    public string UsageFormPercentHeader
    {
        get => _usageFormPercentHeader;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormPercentHeader, value);
    }

    private string _usageFormAddInsCaption = string.Empty;
    /// <summary>The batch the two tables come to, e.g. "1,700 g from Sweet Dough + 20 g of add-ins = a 1,720 g batch."</summary>
    public string UsageFormAddInsCaption
    {
        get => _usageFormAddInsCaption;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormAddInsCaption, value);
    }

    private string _usageFormCostPerPieceText = "—";
    /// <summary>
    /// What a piece of the form's sub-assembly or packaging costs: the piece-size column's first tile, to two decimals
    /// ("0.50"), and the packaging table's footer, to four ("0.5000").
    /// </summary>
    public string UsageFormCostPerPieceText
    {
        get => _usageFormCostPerPieceText;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormCostPerPieceText, value);
    }

    private string _usageFormBatchCostText = "—";
    /// <summary>What the form's whole batch costs, its lines and add-ins together, e.g. "73.98": the piece-size column's second tile.</summary>
    public string UsageFormBatchCostText
    {
        get => _usageFormBatchCostText;
        internal set => this.RaiseAndSetIfChanged(ref _usageFormBatchCostText, value);
    }

    /// <summary>The form's batch weight, which figure stays put, and what a piece costs.</summary>
    internal void Recompute()
    {
        // Packaging is one set of lines per piece, with no batch to share out.
        if (IsFormPackaging)
        {
            var packagingCost = FormPackagingLines.Sum(l => l.CostPerUnit);
            UsageFormCostPerPieceText = FormPackagingLines.Any(l => l.PackagingMaterial is not null) ? $"{packagingCost:N4}" : "—";
            return;
        }

        var batch = Batch();
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(batch);
        UsageFormBatchCostText = batch.Count > 0 ? $"{totalCost:N2}" : "—";
        var authoredValue = AuthoredValue;
        var (weightPerPiece, _) = ProductRules.ResolvePieceSize(AuthoredField, authoredValue, totalWeight);

        UsageFormTotalWeightText = $"{totalWeight:#,0.##} g";
        // Neither figure is needed to put the section in (ProductRules.EnsureValidUsages), so with none typed the
        // caption says what that costs: nothing per piece, until one is. A yield of 1 is what a section in a
        // product-owned role starts at (StartingPieceSize), so it says how to size a bigger batch.
        UsageFormPieceSizeCaption = authoredValue <= 0
            ? "Type the weight per piece or the yield; the other is worked out. Until then this section costs nothing per piece."
            : AuthoredField == UsageAuthoredField.Yield && authoredValue == 1
                ? "One piece takes the whole batch, and still does if the batch changes. For a batch that makes more, type the weight per piece or the yield."
            : AuthoredField == UsageAuthoredField.WeightPerPiece
                ? $"Weight per piece stays at {authoredValue:#,0.##} g if the batch changes."
                : $"The yield stays at {authoredValue:#,0.##} pieces if the batch changes.";

        var costPerPiece = totalWeight > 0 && weightPerPiece > 0
            ? ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, totalWeight)
            : (decimal?)null;
        // Two decimals, as Batch cost beside it; packaging's, above, keeps four.
        UsageFormCostPerPieceText = costPerPiece is { } figure ? $"{figure:N2}" : "—";

        // The formula's base and the batch, given to both tables: an add-in is read against the recipe's base, never its own,
        // and with no base every line of both is read against the whole batch, add-ins included, so they come to 100%.
        var formulaBase = IngredientLineRow.ApplyFormulaBase([.. FormLines], totalWeight);
        FormFormulaBase = formulaBase;
        IngredientLineRow.ApplyFormulaBase(FormAddIns, formulaBase, totalWeight);
        UsageFormPercentHeader = IngredientLineRow.PercentHeader(formulaBase);
        UsageFormBaseCaption = formulaBase > 0
            ? $"Base {formulaBase:#,0.##} g · whole formula {totalWeight / formulaBase * 100m:#,0.#}%"
            : SubAssembliesViewModel.NoBaseCaption;

        // The arithmetic of the two tables, in words: the recipe's grams, this product's on top, the batch.
        var recipeName = EditingUsage?.DisplayName
            ?? EditingUsageSource?.SubAssembly?.Name ?? "the recipe";
        var addInWeight = FormAddIns.Where(l => l.Ingredient is not null).Sum(l => l.Grams);
        var baseWeight = totalWeight - addInWeight;
        UsageFormAddInsCaption = addInWeight > 0
            ? $"{baseWeight:#,0.##} g from {recipeName} + {addInWeight:#,0.##} g of add-ins = a {totalWeight:#,0.##} g batch."
            : $"Nothing extra yet — the batch is {recipeName}'s {baseWeight:#,0.##} g.";
    }

    // An undo step putting the form back (the screen's RestoreSnapshot): each field is set and said to have changed even
    // when it's the same, so a picker bound to it shows it again, and without the setters' own follow-ups.

    /// <summary>Puts the add form's mode back, before the screen rebuilds the option lists it filters.</summary>
    internal void RestoreMode(UsageFormMode mode)
    {
        _editingUsageMode = mode;
        this.RaisePropertyChanged(nameof(EditingUsageMode));
    }

    /// <summary>The role picked, re-pointed at the rebuilt options' copy of it, or dropped when it's gone: no pick, so nothing follows.</summary>
    internal void ShowRole(SubAssemblyRole? role) => this.RaiseAndSetIfChanged(ref _editingUsageRole, role, nameof(EditingUsageRole));

    /// <summary>The source picked, re-pointed at the rebuilt options' copy of it, or dropped when it's gone: no pick, so the form keeps its lines.</summary>
    internal void ShowSource(LinkableSubAssemblyOption? source) =>
        this.RaiseAndSetIfChanged(ref _editingUsageSource, source, nameof(EditingUsageSource));

    /// <summary>Puts the role, source and name back, once the option lists they come from are rebuilt.</summary>
    internal void RestorePicks(SubAssemblyRole? role, LinkableSubAssemblyOption? source, string? name)
    {
        _editingUsageRole = role;
        this.RaisePropertyChanged(nameof(EditingUsageRole));
        _editingUsageSource = source;
        this.RaisePropertyChanged(nameof(EditingUsageSource));
        _editingUsageName = name;
        this.RaisePropertyChanged(nameof(EditingUsageName));
    }

    /// <summary>Puts the piece size back as it stood: which figure was typed, and both figures.</summary>
    internal void RestorePieceSize(UsageAuthoredField authoredField, decimal weightPerPiece, decimal yield)
    {
        AuthoredField = authoredField;
        _editingUsageWeightPerPiece = weightPerPiece;
        this.RaisePropertyChanged(nameof(EditingUsageWeightPerPiece));
        _editingUsageYield = yield;
        this.RaisePropertyChanged(nameof(EditingUsageYield));
    }

    /// <summary>Whether the form adds packaging has followed its mode, role or source: what shows of it follows.</summary>
    internal void RaisePackagingChanged()
    {
        this.RaisePropertyChanged(nameof(IsFormPackaging));
        this.RaisePropertyChanged(nameof(IsFormEditingPackaging));
        this.RaisePropertyChanged(nameof(IsFormNewMovingToSubAssemblies));
    }

    /// <summary>The form's lines or add-ins changed: what its tables and the add-ins badge show follows.</summary>
    internal void RaiseLinesChanged()
    {
        this.RaisePropertyChanged(nameof(IsFormShowingLines));
        this.RaisePropertyChanged(nameof(IsFormShowingAddIns));
        this.RaisePropertyChanged(nameof(FormAddInCount));
        this.RaisePropertyChanged(nameof(HasFormAddIns));
        this.RaisePropertyChanged(nameof(IsFormShowingNoAddIns));
        this.RaisePropertyChanged(nameof(FormAddInsBadgeText));
    }

    public void Dispose()
    {
        _modePicks.Dispose();
        _rolePicks.Dispose();
        _sourcePicks.Dispose();
        _linkTargetPicks.Dispose();
    }
}
