using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.UseCases.RawMaterials;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>
/// One tab of the Raw Materials view (<see cref="RawMaterialsState"/>): a material's form, a new one's, or a copy's. A
/// material's type decides which parts of its details apply — an ingredient is measured in grams and can be made from a
/// sub-assembly or linked to another, a packaging material has a base unit of its own and is always purchased. Its details,
/// alternative units, brand prices and a made ingredient's sub-assembly and add-ins are all edited in memory, and Save writes
/// them together. Its parts each hold a piece of it: the Cost Setup tab (<see cref="PurchaseCost"/>), the Alternative Units
/// tab (<see cref="Units"/>), a made ingredient's recipe (<see cref="Recipe"/>), the Usage Example, Price History and
/// Dependents tabs. What's edited is the draft (<see cref="Draft"/>: the material's own fields, and the Cost Setup, Alternative
/// Units and recipe parts). The form keeps which material it shows, the details it reads, Save through the editor
/// (IRawMaterialEditor), and the undo history over the draft; the view keeps the list, the materials, categories and units of
/// measure its pickers offer, and the load that brings them (<see cref="View"/>).
/// </summary>
public sealed class RawMaterialForm : RecordScreenState<RawMaterialFormSnapshot>, IDisposable, IRecordScreen, ITabForm,
    IRawMaterialDraftHost, IRawMaterialPriceLogHost
{
    /// <summary>The details tabs along the bottom: Cost Setup, Alternative Units, Usage Example, Dependents, then Price History.</summary>
    public const int PriceHistoryTab = 4;

    /// <summary>Dependents: the products the material goes into, with what it makes up of each one's material cost.</summary>
    public const int DependentsTab = 3;

    private readonly RawMaterialsState _view;
    private readonly IRawMaterialQueries _queries;
    private readonly IRawMaterialEditor _editor;
    private readonly IRawMaterialCategoryEditor _categoryEditor;
    private readonly ReadOnlyObservableCollection<RawMaterial> _linkTargets;
    private readonly ReadOnlyObservableCollection<RawMaterial> _addInOptions;
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categoryOptions;

    /// <summary>The form's own lists over the view's materials and categories (its pickers'), let go of as its tab closes.</summary>
    private readonly CompositeDisposable _viewLists = [];

    /// <summary>
    /// Each material picked, by the user or for them (Open, Save's reload), nothing picked included: what loads a material
    /// into the form. Fed by <see cref="SelectedRawMaterial"/>'s setter alone, starting with nothing picked.
    /// </summary>
    private readonly BehaviorSubject<RawMaterial?> _picks = new(null);

    /// <summary>The material picked's id, 0 for none: what the pickers that leave it out (Linked's target, an add-in) follow.</summary>
    private readonly BehaviorSubject<int> _selectedIds = new(0);

    /// <summary>The kind of category the form's type and source can go in: what the Category picker's list follows.</summary>
    private readonly BehaviorSubject<RawMaterialCategoryType> _categoryTypes =
        new(RawMaterialRules.CategoryTypeFor(RawMaterialType.Ingredient, RawMaterialSource.Purchased));

    /// <summary>Each details tab shown, Cost Setup first: the Dependents tab's shares are worked out as it shows.</summary>
    private readonly BehaviorSubject<int> _detailsTabs = new(0);

    /// <summary>The Category picker's naming of a new category (BeginCategoryEdit, CommitCategoryTextAsync).</summary>
    private readonly CategoryPicker<RawMaterialCategory> _categoryPicker;

    // What a new material's Cost Buffer % starts at (Settings → Costing Defaults), as of the view's last load.
    private decimal _defaultCostBufferPercent;

    /// <summary>The material whose price history, brand prices and BOM the tables hold; null for none (a new material).</summary>
    private int? _detailMaterialId;

    /// <param name="queries">What the form reads: a material's details and the copy its Save reads back.</param>
    /// <param name="editor">What the form saves, and checks its tables' rows with.</param>
    /// <param name="categoryEditor">What adds a category named in the Category picker.</param>
    internal RawMaterialForm(
        RawMaterialsState view,
        IRawMaterialQueries queries,
        IRawMaterialEditor editor,
        IRawMaterialCategoryEditor categoryEditor,
        ILogger logger)
        : base(logger)
    {
        _view = view;
        _categoryPicker = NewCategoryPicker();
        _queries = queries;
        _editor = editor;
        _categoryEditor = categoryEditor;
        _defaultCostBufferPercent = view.DefaultCostBufferPercent;

        // The draft and the parts. The draft makes its own parts (Cost Setup, Alternative Units, the recipe) and wires them to
        // each other; each part makes its commands, and subscribes to what it holds alone; what reaches from a part to the
        // form is subscribed below, in the order it always ran. The lists they read are the view's.
        Draft = new RawMaterialDraft(this, new RawMaterialLists(Categories, Ingredients, UnitsOfMeasure), new RawMaterialRowChecks(editor.EnsureValid, editor.EnsureValid));
        Draft.PropertyChanged += (_, e) => DraftChanged(e.PropertyName);
        UsageExample = new RawMaterialUsageExample();
        PriceLog = new RawMaterialPriceLog(this, queries, editor);
        Dependents = new RawMaterialDependents(queries);

        // What follows the form's fields as they change is called by their setters (SelectionChanged here, the draft's own in
        // the draft), in the order it has to run. What follows here runs once now, as the form starts, in the order it always
        // did: several of the subscriptions run as they're made, the picks' included, so each comes after what it reads.

        // The pickers' lists over the view's materials and categories (BindPickerLists).
        (_linkTargets, _addInOptions, _categoryOptions) = BindPickerLists(view);

        // Save offers itself only when the form holds something the saved material doesn't. Measured on every change rather
        // than only when an undo step settles: a step closes on the way out of a field, and a Save button still disabled
        // under the cursor would swallow the very click that enabled it. A table's add row is the same story the other way
        // round - the table raises nothing when it commits, so the step stays open. The draft says when anything it holds
        // changes, its tables' cells included; the form's own fields (which material it is) too, but not the draft's fields
        // it only passes on, which the draft has said already, nor its own word that it has changes to save, which this
        // answers.
        Draft.Changed += History.RefreshDirty;
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(HasUnsavedChanges) && !PassedOnFromDraft.Contains(e.PropertyName ?? string.Empty))
            {
                History.RefreshDirty();
            }
        };

        // The tab follows whether there's anything to save (its dot).
        History.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(History.IsDirty))
            {
                RaisePropertyChanged(nameof(HasUnsavedChanges));
            }
        };

        // The loads a pick, a load and the tabs set off (WireLoads), in their order. Several run as they're made, the picks'
        // included (_picks starts with nothing picked), so they come after what they read.
        WireLoads();

        // The commands. Making one runs nothing.
        SaveCommand = new ScreenCommand(() => RunSaveAsync(SaveAsync), () => History.IsDirty);
        DeleteCommand = new ScreenCommand(() => _view.DeleteAsync(SelectedRawMaterial!, this), () => SelectedRawMaterial is not null);
    }

    /// <summary>
    /// The loads the form's picks and tabs set off, in the order they always ran: a made ingredient's recipe lines, a pick's
    /// material and its details, and the Dependents tiles' shares, each latest-wins (LoadLatest).
    /// </summary>
    private void WireLoads()
    {
        // The Recipe card shows the picked sub-assembly's lines as it's saved: read again on every pick, an undo's included,
        // and on every load, which re-points the pick at the refreshed copy, so a change made on the Sub-Assemblies screen
        // shows here. The totals follow them.
        LoadLatest(Recipe.SubAssemblies, LoadRecipeLinesAsync);

        // A pick loads the material into the form, in these three steps, in this order. Only a pick does (_picks): a load
        // re-points the selection at its fresh copy quietly, so nothing here has to tell the two apart.
        _picks
            .Where(material => material is not null)
            .Subscribe(material => LoadIntoForm(material!));
        _picks.Subscribe(ResetDetails);

        // A newer pick cancels an older one still loading (Switch disposes it, which cancels its token), so a quick second
        // pick never gets the first one's details added to its own. One that fails shows why, and the next pick loads as usual.
        LoadLatest(_picks, LoadDetailsAsync);

        // The Dependents tiles' shares, when the tab shows or its tiles change: a newer ask cancels one still working.
        LoadLatest(
            Dependents.SharesWanted.Merge(_detailsTabs.Select(_ => System.Reactive.Unit.Default)),
            (_, cancellationToken) => SelectedDetailsTab == DependentsTab && SelectedRawMaterial is { } material
                ? Dependents.ShowCostSharesAsync(material.Id, cancellationToken)
                : Task.CompletedTask);
    }

    /// <summary>
    /// The Category picker's naming of a new category: a name typed that isn't one of the material's kind is offered as a new
    /// one (SaveNewCategoryAsync), as one undo step.
    /// </summary>
    private CategoryPicker<RawMaterialCategory> NewCategoryPicker() => new(
        () => EditingCategory, category => AsUndoStep(() => EditingCategory = category), () => CategoryOptions, c => c.Name,
        () => CategorySearchText, text => CategorySearchText = text, async typed => await PromptForNewCategory.Handle(typed),
        SaveNewCategoryAsync, RunSafelyAsync, message => SuccessMessage = message, ex => ShowFailure(ex));

    /// <summary>
    /// The pickers' lists over the view's materials and categories, let go of with the form (_viewLists). Linked's target and an
    /// add-in leave out the ingredient being edited; the Category picker offers only the categories of the material's own kind,
    /// which is all it can be saved in: a made ingredient's are the Made ones (RawMaterialRules.CategoryTypeFor).
    /// </summary>
    private (ReadOnlyObservableCollection<RawMaterial> LinkTargets, ReadOnlyObservableCollection<RawMaterial> AddInOptions,
        ReadOnlyObservableCollection<RawMaterialCategory> CategoryOptions) BindPickerLists(RawMaterialsState view)
    {
        var byName = SortExpressionComparer<RawMaterial>.Ascending(m => m.Name);
        var notTheSelection = _selectedIds
            .DistinctUntilChanged()
            .Select(selectedId => (Func<RawMaterial, bool>)(m => m.Type == RawMaterialType.Ingredient && m.Id != selectedId));
        _viewLists.Add(view.RawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out var linkTargets, byName)
            .Subscribe());
        _viewLists.Add(view.RawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out var addInOptions, byName)
            .Subscribe());
        _viewLists.Add(view.CategoryCache.Connect()
            .Filter(_categoryTypes.Select(type => (Func<RawMaterialCategory, bool>)(c => c.Type == type)))
            .SortAndBind(out var categoryOptions, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe());
        return (linkTargets, addInOptions, categoryOptions);
    }

    /// <summary>The view this form is a tab of: its list, where the form's material is picked, and the load.</summary>
    public RawMaterialsState View => _view;

    /// <summary>
    /// ITabForm: the material the tab is for, as its tab and its address know it: the one picked, from the pick on, not only
    /// once its details are read (<see cref="EditingId"/>), or a link opening another in the preview tab gave the tab the one
    /// before's address meanwhile; 0 for a new one not saved yet.
    /// </summary>
    public int RecordId => SelectedRawMaterial?.Id ?? EditingId;

    /// <summary>ITabForm: the name on the tab, the header's.</summary>
    public string TabTitle => HeaderTitle;

    /// <summary>The material as it's edited: its own fields, and its Cost Setup, Alternative Units and recipe, with their rules.</summary>
    public RawMaterialDraft Draft { get; }

    /// <summary>
    /// The Cost Setup tab: the brand prices, the buffered cost and its target, and the header's saved costs. The draft's, here
    /// too so the page watches it as a part of the form (ScreenBase watches one level down).
    /// </summary>
    public RawMaterialPurchaseCost PurchaseCost => Draft.PurchaseCost;

    /// <summary>The Alternative Units tab: the draft's, as <see cref="PurchaseCost"/> is.</summary>
    public RawMaterialUnits Units => Draft.Units;

    /// <summary>A made ingredient's recipe: the draft's, as <see cref="PurchaseCost"/> is.</summary>
    public RawMaterialRecipe Recipe => Draft.Recipe;

    /// <summary>The Usage Example tab: what a quantity of a material costs.</summary>
    public RawMaterialUsageExample UsageExample { get; }

    /// <summary>The Price History tab: a purchased material's price changes.</summary>
    public RawMaterialPriceLog PriceLog { get; }

    /// <summary>The Dependents tab: the products the material goes into.</summary>
    public RawMaterialDependents Dependents { get; }

    /// <summary>Every raw material, the view's: what a name typed is checked against, and a load finds the material showing in.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _view.RawMaterials;

    /// <summary>
    /// The ingredients only, the view's: packaging never goes in a recipe. Resolves a BOM line's IngredientId and a saved
    /// LinkedToId back to their material; a picker that shouldn't offer the material being edited uses AddInOptions or
    /// LinkTargets instead.
    /// </summary>
    public ReadOnlyObservableCollection<RawMaterial> Ingredients => _view.Ingredients;

    /// <summary>The ingredients a linked ingredient can be linked to: every one but the material being edited.</summary>
    public ReadOnlyObservableCollection<RawMaterial> LinkTargets => _linkTargets;

    /// <summary>The Add-Ins table's ingredient picker's list: every ingredient but the made one being edited — a made ingredient can't be its own add-in.</summary>
    public ReadOnlyObservableCollection<RawMaterial> AddInOptions => _addInOptions;

    /// <summary>Every category, the view's.</summary>
    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _view.Categories;

    /// <summary>
    /// What the Category picker lists: the categories of the material's type. A name that isn't one of them is added by
    /// typing it (CommitCategoryTextAsync).
    /// </summary>
    public ReadOnlyObservableCollection<RawMaterialCategory> CategoryOptions => _categoryOptions;

    /// <summary>Every unit of measure, the view's: for the base unit picker and the Alternative Units table's unit cell.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure => _view.UnitsOfMeasure;

    public IReadOnlyList<RawMaterialType> Types { get; } = Enum.GetValues<RawMaterialType>();

    public IReadOnlyList<RawMaterialSource> Sources { get; } = Enum.GetValues<RawMaterialSource>();

    private string _categorySearchText = string.Empty;
    /// <summary>
    /// What's typed into the Category picker (bound to its Text): settled into a category, or a new one, once the picker is
    /// left (CommitCategoryTextAsync).
    /// </summary>
    public string CategorySearchText
    {
        get => _categorySearchText;
        set => RaiseAndSetIfChanged(ref _categorySearchText, value);
    }

    private RawMaterial? _selectedRawMaterial;
    /// <summary>
    /// The material the form is about. Only <see cref="Pick"/> loads it into the form (_picks): a load points it at its
    /// fresh copy, leaving what's typed alone.
    /// </summary>
    public RawMaterial? SelectedRawMaterial
    {
        get => _selectedRawMaterial;
        private set
        {
            if (RaiseAndSetIfChanged(ref _selectedRawMaterial, value))
            {
                RaisePropertyChanged(nameof(RecordId));
                SelectionChanged();
            }
        }
    }

    private int _editingId;
    /// <summary>The material the form holds: 0 for a new one, or a copy, not saved yet.</summary>
    public int EditingId
    {
        get => _editingId;
        private set
        {
            if (RaiseAndSetIfChanged(ref _editingId, value))
            {
                RaisePropertyChanged(nameof(RecordId));
            }
        }
    }

    /// <summary>
    /// The user picks <paramref name="material"/> (a list's row, Open), or nothing, as New does: it's loaded into the form,
    /// unless it's the one showing.
    /// </summary>
    public void Pick(RawMaterial? material)
    {
        if (material != SelectedRawMaterial)
        {
            SelectedRawMaterial = material;
            _picks.OnNext(material);
        }
    }

    /// <summary>
    /// What follows the material showing, a pick's or a load's fresh copy: the list's row, the pickers that leave it out,
    /// the details tab, and the header's saved costs (a new Per unit, or a unit's multiplier, updates them through
    /// UpdatePurchaseCostTexts).
    /// </summary>
    private void SelectionChanged()
    {
        // The view's list follows the material showing while this form shows, and a material opened, by its Save
        // included, ends the new one's row.
        _view.FormShowsAnother(this, SelectedRawMaterial is not null);
        RaisePropertyChanged(nameof(TabTitle));
        _selectedIds.OnNext(SelectedRawMaterial?.Id ?? 0);

        // A hidden tab can't stay the one showing: picking a made or new material while on Price History goes back to Cost
        // Setup.
        if (SelectedRawMaterial is not { Source: RawMaterialSource.Purchased } && SelectedDetailsTab == PriceHistoryTab)
        {
            SelectedDetailsTab = 0;
        }

        History.RefreshDirty();
    }

    private bool _areDetailsReady = true;
    /// <summary>
    /// False from picking another material until its details (price history, brand prices, BOM) arrive, while the previous
    /// material's still show: the details tabs ignore the mouse, and Save waits, since it would write the previous
    /// material's brand prices or BOM under this one.
    /// </summary>
    public bool AreDetailsReady
    {
        get => _areDetailsReady;
        private set => RaiseAndSetIfChanged(ref _areDetailsReady, value);
    }

    /// <summary>The draft's fields the form passes on under the names the page binds, said to have changed as they do (DraftChanged).</summary>
    private static readonly HashSet<string> PassedOnFromDraft =
    [
        nameof(EditingName), nameof(EditingType), nameof(EditingCategory), nameof(EditingSource), nameof(EditingWeightLossPercent),
        nameof(EditingLinkedTo), nameof(EditingWeightSharePercent), nameof(EditingCostSharePercent), nameof(EditingBaseUnit),
        nameof(CanChangeTypeAndSource),
    ];

    /// <summary>The name typed, which the header and the tab show: the draft's.</summary>
    public string EditingName
    {
        get => Draft.Name;
        set => Draft.Name = value;
    }

    /// <summary>The material's type, as loaded, put back or picked (<see cref="ChangeType"/>): the draft's.</summary>
    public RawMaterialType EditingType => Draft.Type;

    /// <summary>The user picks the type, on a new material: what the other type had filled in goes (RawMaterialDraft.ChangeType).</summary>
    public void ChangeType(RawMaterialType type) => Draft.ChangeType(type);

    public RawMaterialCategory? EditingCategory
    {
        get => Draft.Category;
        set => Draft.Category = value;
    }

    /// <summary>The material's source, as loaded, put back or picked (<see cref="ChangeSource"/>): the draft's.</summary>
    public RawMaterialSource EditingSource => Draft.Source;

    /// <summary>The user picks the source, on a new material (RawMaterialDraft.ChangeSource).</summary>
    public void ChangeSource(RawMaterialSource source) => Draft.ChangeSource(source);

    /// <summary>Made only: the batch's weight loss, the draft's.</summary>
    public decimal? EditingWeightLossPercent
    {
        get => Draft.WeightLossPercent;
        set => Draft.WeightLossPercent = value;
    }

    /// <summary>Linked only: the ingredient this one is a part of, the draft's.</summary>
    public RawMaterial? EditingLinkedTo
    {
        get => Draft.LinkedTo;
        set => Draft.LinkedTo = value;
    }

    /// <summary>Linked only: how much of the linked-to ingredient's weight this part is, the draft's.</summary>
    public decimal? EditingWeightSharePercent
    {
        get => Draft.WeightSharePercent;
        set => Draft.WeightSharePercent = value;
    }

    /// <summary>Linked only: how much of the linked-to ingredient's cost this part carries, the draft's.</summary>
    public decimal? EditingCostSharePercent
    {
        get => Draft.CostSharePercent;
        set => Draft.CostSharePercent = value;
    }

    /// <summary>Packaging only: the base unit, as loaded, put back or picked (<see cref="ChangeBaseUnit"/>), the draft's.</summary>
    public UnitOfMeasure? EditingBaseUnit => Draft.BaseUnit;

    /// <summary>The user picks the base unit, on a new packaging material (RawMaterialDraft.ChangeBaseUnit).</summary>
    public void ChangeBaseUnit(UnitOfMeasure? unit) => Draft.ChangeBaseUnit(unit);

    /// <summary>Only a new material's type and source can be picked: the draft's.</summary>
    public bool CanChangeTypeAndSource => Draft.CanChangeTypeAndSource;

    /// <summary>
    /// What follows a change of the draft's: the form's own names for its fields said to have changed too, the tab its name,
    /// and the Category picker the kind of category it can go in.
    /// </summary>
    private void DraftChanged(string? propertyName)
    {
        switch (propertyName)
        {
            case nameof(RawMaterialDraft.Name):
                RaisePropertyChanged(nameof(EditingName));
                RaisePropertyChanged(nameof(TabTitle));
                break;
            case nameof(RawMaterialDraft.Type):
                RaisePropertyChanged(nameof(EditingType));
                break;
            case nameof(RawMaterialDraft.Category):
                RaisePropertyChanged(nameof(EditingCategory));
                break;
            case nameof(RawMaterialDraft.Source):
                RaisePropertyChanged(nameof(EditingSource));
                break;
            case nameof(RawMaterialDraft.WeightLossPercent):
                RaisePropertyChanged(nameof(EditingWeightLossPercent));
                break;
            case nameof(RawMaterialDraft.LinkedTo):
                RaisePropertyChanged(nameof(EditingLinkedTo));
                break;
            case nameof(RawMaterialDraft.WeightSharePercent):
                RaisePropertyChanged(nameof(EditingWeightSharePercent));
                break;
            case nameof(RawMaterialDraft.CostSharePercent):
                RaisePropertyChanged(nameof(EditingCostSharePercent));
                break;
            case nameof(RawMaterialDraft.BaseUnit):
                RaisePropertyChanged(nameof(EditingBaseUnit));
                break;
            case nameof(RawMaterialDraft.CanChangeTypeAndSource):
                RaisePropertyChanged(nameof(CanChangeTypeAndSource));
                break;
            case nameof(RawMaterialDraft.CategoryType):
                _categoryTypes.OnNext(Draft.CategoryType);
                break;
        }
    }

    private bool _canChangeBaseUnit = true;
    /// <summary>
    /// A packaging material's base unit can be picked while it's new, or has no saved alternative units or prices yet
    /// (RawMaterialRules.EnsureBaseUnitCanChange; the repository also checks packaging lines). Unsaved units don't count:
    /// they're saved against whichever base unit is picked then.
    /// </summary>
    public bool CanChangeBaseUnit
    {
        get => _canChangeBaseUnit;
        private set => RaiseAndSetIfChanged(ref _canChangeBaseUnit, value);
    }

    // The form's cards follow its type and source, not the selection, so a new material can be filled in completely before
    // its one Save; the Price History tab follows a purchased material, and the header its name.
    public bool IsEditingIngredient => Draft.IsEditingIngredient;

    public bool IsEditingPackaging => Draft.IsEditingPackaging;

    /// <summary>A made ingredient: it has a sub-assembly, add-ins and a weight loss percent instead of a purchase price.</summary>
    public bool IsEditingMade => Draft.IsEditingMade;

    /// <summary>A linked ingredient: its cost is a share of another ingredient's, instead of a price or a BOM.</summary>
    public bool IsEditingLinked => Draft.IsEditingLinked;

    /// <summary>What the link works out to as it's filled in: the linked-to ingredient's cost per gram, and this part's.</summary>
    public string LinkedCostPreviewText => LinkedCostPreview(EditingLinkedTo, EditingWeightSharePercent, EditingCostSharePercent);

    /// <summary>The linked-to ingredient's other parts, and how much of its weight and cost all of them take with this one.</summary>
    public string LinkedPartsText => LinkedPartsSummary(EditingLinkedTo, EditingWeightSharePercent, EditingCostSharePercent);

    /// <summary>A purchased material, saved or still being filled in, so a new one can be priced before its first Save.</summary>
    public bool ShowPurchaseSetup => Draft.ShowPurchaseSetup;

    /// <summary>A saved purchased material: a new one has no price history yet.</summary>
    public bool ShowPriceHistory => SelectedRawMaterial is { Source: RawMaterialSource.Purchased };

    private int _selectedDetailsTab;
    /// <summary>
    /// The details tab showing. Moves to Cost Setup when Price History is about to hide (SelectionChanged); the Dependents
    /// tab's shares are worked out as it shows.
    /// </summary>
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set
        {
            if (RaiseAndSetIfChanged(ref _selectedDetailsTab, value))
            {
                _detailsTabs.OnNext(value);
            }
        }
    }

    /// <summary>The summary header's big title: the material's name, or "New raw material".</summary>
    public string HeaderTitle =>
        SelectedRawMaterial is null && string.IsNullOrWhiteSpace(EditingName) ? "New raw material" : EditingName;

    /// <summary>The form's base unit name: "gram" for an ingredient, e.g. "centimeter" for ribbon.</summary>
    public string BaseUnitName => Draft.BaseUnitName;

    public string BaseUnitAbbreviation => Draft.BaseUnitAbbreviation;

    /// <summary>
    /// The Base Unit tile: "gram (g)" for an ingredient, and a packaging material's base unit as last saved, e.g.
    /// "centimeter (cm)", or "—" until its first Save. The unit is picked in the fields row (EditingBaseUnit), so
    /// the tile shows what's saved, as the cost tiles beside it do.
    /// </summary>
    public string SavedBaseUnitText => SavedBaseUnitTextOf(EditingType, SelectedRawMaterial);

    private string _lastUpdatedText = "—";
    /// <summary>The day the material's cost last changed, or "—" for one whose cost has never moved.</summary>
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    /// <summary>The view's load: its lists read again, and every open form kept as it is (RawMaterialsState.LoadCommand).</summary>
    public ScreenCommand LoadCommand => _view.LoadCommand;

    /// <summary>The view's New: a new material, in the preview tab or a tab of its own (RawMaterialsState.NewCommand).</summary>
    public ScreenCommand NewCommand => _view.NewCommand;

    public ScreenCommand SaveCommand { get; }

    public ScreenCommand DeleteCommand { get; }

    /// <summary>
    /// Input: the name typed into the Category picker that isn't a category yet, which the prompt starts with so it
    /// needn't be typed twice. Output: the name to add it as, or null if it was cancelled. The page answers it with a dialog
    /// (Prompts).
    /// </summary>
    public Interaction<string, string?> PromptForNewCategory { get; } = new();

    /// <summary>
    /// Flags EditingName as taken the moment it's left, rather than waiting for Save to hit the same message from the
    /// unique-index violation (ScreenState.RunSafelyAsync). Trimmed, as Save stores it, and case-insensitive, as the
    /// collation the unique index relies on is. Fixing the name and leaving the field again clears the message this raised,
    /// without touching an unrelated one.
    /// </summary>
    public void CheckNameAvailability()
    {
        var name = EditingName.Trim();
        var isTaken = name.Length > 0
            && RawMaterials.Any(m => m.Id != _editingId && string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));

        if (isTaken)
        {
            ErrorMessage = "That name is already in use.";
        }
        else if (ErrorMessage == "That name is already in use.")
        {
            ErrorMessage = null;
        }
    }

    /// <summary>The Category picker took the cursor (CategoryPicker.Begin).</summary>
    public void BeginCategoryEdit() => _categoryPicker.Begin();

    /// <summary>
    /// What's typed into the Category picker, settled as it's left (CategoryPicker.CommitAsync): a name that isn't one of
    /// the material type's categories is offered as a new one of that type.
    /// </summary>
    public Task CommitCategoryTextAsync() => _categoryPicker.CommitAsync();

    /// <summary>The material whose details the tables hold, which the view's load reads the products using it for; null for none.</summary>
    internal int? DetailMaterialId => _detailMaterialId;

    /// <summary>
    /// What the form was showing as the view's load merges in, by id, taken before anything it reads is replaced (a picker
    /// whose item gets replaced can clear its own selection mid-merge), and what the load found.
    /// </summary>
    internal sealed class Load
    {
        public int? SelectedId { get; init; }

        public int? CategoryId { get; init; }

        public int? LinkedToId { get; init; }

        public int? SubAssemblyId { get; init; }

        public int? BaseUnitId { get; init; }

        public int? ExampleMaterialId { get; init; }

        public RawMaterialUnitOption? ExampleUnit { get; init; }

        /// <summary>
        /// The material showing was saved since the form took it (SavedVersion) with nothing typed: it's read into the form
        /// again, so what it shows, and what its next Save starts from, is what's saved now (FollowSavedVersion).
        /// </summary>
        public bool SavedSince { get; set; }
    }

    /// <summary>The view's load is about to merge: what the form shows now, by id.</summary>
    internal Load BeginLoad() => new()
    {
        SelectedId = SelectedRawMaterial?.Id,
        CategoryId = EditingCategory?.Id,
        LinkedToId = EditingLinkedTo?.Id,
        SubAssemblyId = Recipe.SubAssembly?.Id,
        BaseUnitId = EditingBaseUnit?.Id,
        ExampleMaterialId = UsageExample.Material?.Id,
        ExampleUnit = UsageExample.Unit,
    };

    /// <summary>
    /// The view's load merges into the form, its lists merged already: the pickers, the recipe and the selection pointed at
    /// their fresh copies, the form and what's typed left as they are. Run within the form's own refresh, so nothing it
    /// writes is the user's.
    /// </summary>
    internal void TakeLoad(Load load, IReadOnlyList<SubAssembly> subAssemblies, decimal defaultCostBufferPercent)
    {
        // The draft takes the load's fresh copies of what it refers to, nothing typed changed (RawMaterialDraft.Rebase).
        Draft.Rebase(load.CategoryId, load.LinkedToId, load.SubAssemblyId, load.BaseUnitId, subAssemblies);

        SelectedRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == load.SelectedId);

        // Every load, so a list's own save is followed by the load it asked for and no later one.
        load.SavedSince = FollowSavedVersion(SelectedRawMaterial is { } showing && showing.Id == EditingId ? showing.UpdatedAt : SavedVersion);

        RefreshDefaultCostBuffer(defaultCostBufferPercent);

        UsageExample.Refresh(RawMaterials.FirstOrDefault(m => m.Id == load.ExampleMaterialId), load.ExampleUnit);
    }

    /// <summary>
    /// The view's load is merged: what follows it on the form. <paramref name="dependentsRead"/> holds the products using
    /// each material whose details showed as the load set out, read with the lists.
    /// </summary>
    internal async Task EndLoadAsync(Load load, IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>> dependentsRead)
    {
        // The Sub-Assemblies screen may have changed the recipe's lines. Pointing the recipe at its fresh copy reads them only
        // when the sub-assembly's own row changed, so they're read here every time.
        if (Recipe.SubAssembly is { } recipe)
        {
            await LoadRecipeLinesAsync(recipe, CancellationToken.None);
        }

        // The products using the material, read with the lists: kept only if the details showing are still that material's.
        if (_detailMaterialId is { } readFor && dependentsRead.TryGetValue(readFor, out var dependents))
        {
            Dependents.Show(dependents);
        }

        if (load.SavedSince && SelectedRawMaterial is { } savedAgain)
        {
            await ReloadSavedRawMaterialAsync(savedAgain.Id);
        }

        // After the merge, which only points the selection at its fresh copy: a material asked for before the list held it
        // has to load its form.
        OpenRecordAskedFor();
    }

    /// <summary>Save: the material with its units, brand prices or add-ins, all or nothing (IRawMaterialEditor.SaveAsync).</summary>
    private async Task SaveAsync()
    {
        // The picked material's details haven't replaced the previous one's yet: saving now would write that material's
        // brand prices or BOM under this one. The moment passes in a fraction of a second.
        if (!AreDetailsReady)
        {
            return;
        }

        // The material first, then its tables as they're typed (a half-filled or repeated row refused, naming it).
        var material = Draft.ToMaterial(_editingId, SavedVersionToSend);
        _editor.EnsureValid(material);
        var id = await _editor.SaveAsync(Draft.ToSaveRequest(material), CancellationToken.None);

        // The list picks up any cost that cascaded to other materials; then the form reloads what
        // was saved, so new units get their ids and the brand prices, price history and BOM show the new rows.
        await LoadCommand.ExecuteAsync();
        await ReloadSavedRawMaterialAsync(id);
        SuccessMessage = $"{EditingName} was saved.";
    }

    /// <summary>
    /// New, from the header or the list, in this form. A material still waiting for the load to open it is no longer opened:
    /// it would load over the new form, and a name typed into it since would then be saved as that material's.
    /// </summary>
    internal void StartNew(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        ForgetRecordToOpen();
        ClearForm(type, categoryId, source);
    }

    /// <summary>
    /// Copy, from the list: a new material of <paramref name="material"/>'s type, in its category, with its base unit,
    /// named "… Copy" ("… Copy 2" when that's taken). A made ingredient's copy is a made one, as its category holds only
    /// those; a linked one's is purchased. The name and the base unit come after the form is settled, so the copy is
    /// there to be saved, and closing its tab asks first.
    /// </summary>
    internal void StartCopyOf(RawMaterial material)
    {
        StartNew(
            material.Type,
            material.CategoryId,
            material.Source == RawMaterialSource.Made ? RawMaterialSource.Made : RawMaterialSource.Purchased);
        EditingName = CopyNameFor(material.Name);
        if (material.Type == RawMaterialType.Packaging)
        {
            ChangeBaseUnit(UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId));
        }
    }

    /// <summary>"Disposable Spoon Copy", or the first of "… Copy 2", "… Copy 3" and on that no material has yet.</summary>
    private string CopyNameFor(string name)
    {
        var copy = $"{name} Copy";
        for (var n = 2; RawMaterials.Any(m => string.Equals(m.Name, copy, StringComparison.OrdinalIgnoreCase)); n++)
        {
            copy = $"{name} Copy {n}";
        }

        return copy;
    }

    /// <summary>
    /// An empty form, for New, and for a revert of a new material. It starts as a purchased ingredient, the usual kind, but
    /// the category, brand prices and the usage example's material start empty, so none is saved or costed by accident. The
    /// numbers keep a value: the cost buffer is Settings' default, and the usage example's quantity stays. The list's New …
    /// Here starts it as <paramref name="type"/> in <paramref name="categoryId"/> instead, and as a made ingredient
    /// (<paramref name="source"/>) in a Made category, set before the form is settled, so it isn't taken for an edit to save.
    /// </summary>
    private void ClearForm(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        // The new material's row, if one was named in the list, goes with the form it stood for.
        if (_view.Form == this)
        {
            _view.List.EndNewRow();
        }

        History.Clear();
        FormVersion++;
        EditingId = 0;
        SavedVersion = null;
        Pick(null);
        Draft.Clear(type, categoryId, source, _defaultCostBufferPercent);
        UsageExample.Pick(null);
        ClearDetails();
    }

    private void LoadIntoForm(RawMaterial material)
    {
        History.Clear();

        // Not for the same material loaded again, as a Save does: the boxes still hold what's its.
        if (material.Id != _editingId)
        {
            FormVersion++;
        }

        EditingId = material.Id;
        SavedVersion = material.UpdatedAt;
        Draft.Show(material);
    }

    private void UpdateCanChangeBaseUnit() =>
        CanChangeBaseUnit = SelectedRawMaterial is null
            || (SelectedRawMaterial.Units.Count == 0 && PurchaseCost.SavedBrandPriceCount == 0 && PriceLog.Entries.Count == 0);

    /// <summary>
    /// What follows a new selection: its alternative units, and nothing picked or open. Another material's price history,
    /// brand prices and BOM replace the ones showing when they arrive, in the same pass (LoadDetailsAsync): cleared here,
    /// the tables drew empty in between. Until then they aren't this material's (AreDetailsReady).
    /// </summary>
    private void ResetDetails(RawMaterial? material)
    {
        AreDetailsReady = material is null || material.Id == _detailMaterialId;
        PriceLog.SelectedEntry = null;
        PurchaseCost.SelectedBrandPriceRow = null;
        Recipe.SelectedAddIn = null;
        Units.Load(material);
        PurchaseCost.RefreshBrandPriceRows();
        UpdateCanChangeBaseUnit();
        if (material is not null)
        {
            UsageExample.Pick(material);
        }
    }

    /// <summary>
    /// A new material's details: no alternative units, brand prices, price history or BOM lines. Selecting nothing
    /// clears them only when something was selected, and after an unsaved new material nothing was.
    /// </summary>
    private void ClearDetails()
    {
        ResetDetails(null);
        ShowDetails(null, RawMaterialDetail.None);
    }

    /// <summary>
    /// The recipe's lines: <paramref name="subAssembly"/>'s as it's saved (IRawMaterialQueries.ReadSubAssemblyLinesAsync),
    /// which the draft's recipe shows. None for no pick.
    /// </summary>
    private async Task LoadRecipeLinesAsync(SubAssembly? subAssembly, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubAssemblyLine> lines = subAssembly is null
            ? []
            : await _queries.ReadSubAssemblyLinesAsync(subAssembly.Id, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        Recipe.ShowLines(lines);
    }

    /// <summary>
    /// What a pick shows beyond the list's copy of the material (its price history, brand prices and BOM), read on the thread
    /// pool in one go, so the UI thread comes back once rather than after each read. Only the material picked last is shown:
    /// a newer pick cancels this load, so its details can't join the newer one's.
    /// </summary>
    private async Task LoadDetailsAsync(RawMaterial? material, CancellationToken cancellationToken)
    {
        if (material is null)
        {
            ShowDetails(null, RawMaterialDetail.None);
            return;
        }

        RawMaterialDetail detail;
        try
        {
            detail = await Task.Run(() => _queries.ReadDetailAsync(material, cancellationToken), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && material.Id != _detailMaterialId)
        {
            // Nothing of the picked material's came: the previous material's mustn't stay showing under its name.
            ShowDetails(null, RawMaterialDetail.None);
            throw;
        }

        cancellationToken.ThrowIfCancellationRequested();
        ShowDetails(material, detail);
    }

    /// <summary>
    /// Puts <paramref name="material"/>'s details in place of whatever the tables held, in one pass, so nothing is drawn in
    /// between, along with what Save compares the BOM against. None for no material.
    /// </summary>
    private void ShowDetails(RawMaterial? material, RawMaterialDetail detail)
    {
        PriceLog.Show(material, detail.PriceHistory);
        ShowUpdatedAt(material);

        // The recipe's own lines show from the sub-assembly picked (LoadRecipeLinesAsync); only the add-ins are the material's.
        Recipe.ShowAddIns(detail.BomLines);
        PurchaseCost.ShowBrandPrices(detail.BrandPrices);
        Dependents.Show(detail.Dependents);

        UpdateCanChangeBaseUnit();
        PurchaseCost.RebuildBufferedCostUnitOptions();
        PurchaseCost.BufferedCostUnit = PurchaseCost.DearestBrandUnit();
        _detailMaterialId = material?.Id;
        AreDetailsReady = true;
        Recipe.BomInputsChanged();

        // Everything the form holds is now the material's own, so this is what Save's dirty check measures
        // against. Taken here rather than where the form is filled in: the units, brand prices and BOM land a
        // moment after the rest, and a form measured before them would read as changed when they arrived.
        History.MarkClean();
    }

    /// <summary>
    /// The Last Updated tile: the day the material's cost last changed (CostUpdatedAt), and nothing else. Not the newest price
    /// history entry's date, which a made or linked ingredient never has, so it would show "—" for them however recently
    /// their cost had moved. The material also carries UpdatedAt, the day the form last saved it; nothing shows it.
    /// </summary>
    private void ShowUpdatedAt(RawMaterial? material) =>
        LastUpdatedText = material?.CostUpdatedAt?.ToString("MMM d, yyyy", CultureInfo.CurrentCulture) ?? "—";

    /// <summary>
    /// Takes up Settings' cost buffer for a new material as LoadCommand read it. A new material still showing the one it
    /// started with takes the new one, and what Save measures against follows it, since the change isn't the user's; one
    /// whose buffer was typed keeps it.
    /// </summary>
    private void RefreshDefaultCostBuffer(decimal defaultCostBufferPercent)
    {
        var untouchedNewMaterial = _editingId == 0 && PurchaseCost.EditingCostBufferPercent == _defaultCostBufferPercent;
        _defaultCostBufferPercent = defaultCostBufferPercent;
        if (untouchedNewMaterial)
        {
            PurchaseCost.EditingCostBufferPercent = defaultCostBufferPercent;
            PurchaseCost.EditingTargetCostBufferPercent = defaultCostBufferPercent;
            History.Rebase((settled, now) => settled with { CostBufferPercent = now.CostBufferPercent, TargetCostBufferPercent = now.TargetCostBufferPercent });
        }
    }

    /// <summary>
    /// Re-fetches the material just saved and pushes it into the view's list and the selection. A fresh copy is always a
    /// new selection, so the form and its details reload from what was saved. A new material, saved for the first time, is
    /// still the one whose details show, not another material: they stay until the reload replaces them, as an existing
    /// one's do.
    /// </summary>
    private async Task ReloadSavedRawMaterialAsync(int id)
    {
        var saved = await _queries.ReadAsync(id, CancellationToken.None);
        if (saved is not null)
        {
            _view.TakeSaved(saved);
            _detailMaterialId = id;
            Pick(saved);
        }
    }

    /// <summary>
    /// Saves a category of the material's own type, the only kind the picker offers it (a Made one for a made ingredient),
    /// and reads it back with the screen's load; null, with the reason showing, if its name is refused.
    /// </summary>
    private async Task<RawMaterialCategory?> SaveNewCategoryAsync(string name)
    {
        var type = RawMaterialRules.CategoryTypeFor(EditingType, EditingSource);
        try
        {
            _categoryEditor.EnsureValid(name, type);
        }
        catch (BusinessRuleException refused)
        {
            ErrorMessage = refused.Message;
            return null;
        }

        // Saved with its id, which the reloaded copy to pick is found by.
        var category = await _categoryEditor.AddAsync(name, type, CancellationToken.None);
        await LoadCommand.ExecuteAsync();
        return Categories.FirstOrDefault(c => c.Id == category.Id);
    }

    protected override int? FindRecordByName(string text) => RecordNameMatch.Best(RawMaterials, material => material.Name, text)?.Id;

    protected override bool ReplacesShowing(int id) => id != SelectedRawMaterial?.Id;

    protected override bool TrySelectRecord(int id)
    {
        if (RawMaterials.FirstOrDefault(m => m.Id == id) is not { } material)
        {
            return false;
        }

        var list = _view.List;
        if (!list.VisibleRawMaterials.Contains(material))
        {
            list.SearchText = string.Empty;
        }

        Pick(material);
        return true;
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: the view opens it, in its tab or the preview tab.</summary>
    bool IRecordScreen.OpenByName(string text) => _view.OpenByName(text);

    /// <summary>
    /// "Egg costs 0.0900/g, buffered 0.0990/g. At 70% of its weight for 30% of its cost, this costs 0.0386/g, buffered 0.0424/g."
    /// Worked out with the same rule the cascade saves with, from the linked-to ingredient's stored costs: the
    /// same share of its true cost and of its (fixed) buffered cost.
    /// </summary>
    private static string LinkedCostPreview(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return "Pick the ingredient this one is a part of.";
        }

        var linkedToText = $"{linkedTo.Name} costs {linkedTo.CostPerBaseUnit:N4}/g, buffered {linkedTo.BufferedCostPerBaseUnit:N4}/g.";
        if (weightShare is not (> 0 and <= 100) || costShare is not (> 0 and <= 100))
        {
            return $"{linkedToText} Enter this part's share of its weight and of its cost.";
        }

        var cost = RawMaterialRules.LinkedCost(linkedTo.CostPerBaseUnit, weightShare.Value, costShare.Value);
        var buffered = RawMaterialRules.LinkedCost(linkedTo.BufferedCostPerBaseUnit, weightShare.Value, costShare.Value);
        return $"{linkedToText} At {weightShare:0.##}% of its weight for {costShare:0.##}% of its cost, this costs {cost:N4}/g, buffered {buffered:N4}/g.";
    }

    /// <summary>
    /// "Also linked to Egg: Egg Yolk (30% of the weight, 70% of the cost). With this one, 100% of its weight and 100% of its cost."
    /// The other parts come from the list's copies, so they're as of the last load; Save checks the saved ones.
    /// </summary>
    private string LinkedPartsSummary(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return string.Empty;
        }

        var others = RawMaterials
            .Where(m => m.LinkedToId == linkedTo.Id && m.Id != _editingId)
            .ToList();
        var totalWeight = others.Sum(m => m.WeightSharePercent ?? 0m) + (weightShare ?? 0m);
        var totalCost = others.Sum(m => m.CostSharePercent ?? 0m) + (costShare ?? 0m);
        var totals = $"{totalWeight:0.##}% of its weight and {totalCost:0.##}% of its cost";

        if (others.Count == 0)
        {
            return $"Nothing else is linked to {linkedTo.Name}. This part takes {totals}.";
        }

        var parts = string.Join(", ", others.Select(m => $"{m.Name} ({m.WeightSharePercent:0.##}% of the weight, {m.CostSharePercent:0.##}% of the cost)"));
        return $"Also linked to {linkedTo.Name}: {parts}. With this one, {totals} (at most 100% each).";
    }

    /// <summary>
    /// SavedBaseUnitText. The saved base unit is looked up in the Units of Measure list, as the form's picker is, since
    /// the list keeps it current through a rename; the material's own BaseUnit is the fallback before the list loads.
    /// </summary>
    private string SavedBaseUnitTextOf(RawMaterialType type, RawMaterial? saved)
    {
        if (type != RawMaterialType.Packaging)
        {
            return "gram (g)";
        }

        var unit = saved?.BaseUnitId is { } id ? UnitsOfMeasure.FirstOrDefault(u => u.Id == id) ?? saved.BaseUnit : null;
        return unit is null ? "—" : $"{unit.Name} ({unit.Abbreviation})";
    }

    protected override RawMaterialFormSnapshot CaptureSnapshot() => Draft.Capture(_editingId);

    /// <summary>
    /// Puts the form back as <paramref name="snapshot"/> had it: which material it is, and the draft
    /// (RawMaterialDraft.Restore), its fields within the form's restore, so nothing written is taken for the user's.
    /// </summary>
    protected override void RestoreSnapshot(RawMaterialFormSnapshot snapshot, RawMaterialFormSnapshot? settledThen)
    {
        ErrorMessage = null;
        Draft.Restore(snapshot, put => ApplyRestore(() =>
        {
            EditingId = snapshot.EditingId;
            put();
        }));
    }

    /// <summary>
    /// Nothing is recorded until a material's details have arrived, or while the screen is being written to wholesale:
    /// the brand prices and the BOM land a moment after the pick (AreDetailsReady), and a step taken before they do would
    /// put the empty tables back over them.
    /// </summary>
    protected override bool CanRecordUndo => AreDetailsReady && !IsBusyApplying;

    protected override string UnsavedChangesName =>
        !string.IsNullOrWhiteSpace(EditingName) ? EditingName.Trim() : SelectedRawMaterial?.Name ?? "the new raw material";

    protected override Task SaveFormAsync() => SaveCommand.ExecuteAsync();

    protected override Task ReadListsAgainAsync() => LoadCommand.ExecuteAsync();

    protected override int ShowingRecordId => _editingId;

    /// <summary>
    /// Don't Save: the form goes back to the material as it was last saved, read again, since its alternative units write
    /// through to the copy the list holds. A new material was never saved, so it goes back to a blank one.
    /// </summary>
    protected override async Task RevertToSavedAsync()
    {
        if (SelectedRawMaterial is { } material)
        {
            await ReloadSavedRawMaterialAsync(material.Id);
        }
        else
        {
            ClearForm();
        }
    }

    /// <summary>The name typed into the new material's row in the list goes into the form, as typed there: one undo step.</summary>
    internal void NameNew(string name) => AsUndoStep(() => EditingName = name);

    /// <summary>The new material's row went with Esc: the name the form was given goes too, as no edit of the user's.</summary>
    internal void ClearNewName() => EditingName = string.Empty;

    /// <summary>The material the form shows was renamed in the list, saved: the form and its undo steps take the name as given.</summary>
    internal void TakeRename(string name)
    {
        EditingName = name;
        History.RewriteAll(snapshot => snapshot with { Name = name });
    }

    /// <summary>The material the form shows was moved to another category, saved: the form and its undo steps take it as given.</summary>
    internal void TakeMove(int categoryId)
    {
        EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
        History.RewriteAll(snapshot => snapshot with { CategoryId = categoryId });
    }

    /// <summary>A category was deleted: the form's undo steps forget it, so none puts back a category there's no longer any of.</summary>
    internal void ForgetCategory(int categoryId) =>
        History.RewriteAll(snapshot => snapshot.CategoryId == categoryId ? snapshot with { CategoryId = null } : snapshot);

    // What the draft and the Price History tab ask of the form.
    bool IRawMaterialDraftHost.IsBusyApplying => IsBusyApplying;

    void IRawMaterialPriceLogHost.PricesDeleted() => UpdateCanChangeBaseUnit();

    public void Dispose()
    {
        // The form's lists over the view's, which would otherwise keep it as long as the view lives.
        _viewLists.Dispose();
        Dependents.Dispose();
        Draft.Dispose();
        _picks.Dispose();
        _selectedIds.Dispose();
        _categoryTypes.Dispose();
        _detailsTabs.Dispose();
    }
}
