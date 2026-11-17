using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using ILogger = Serilog.ILogger;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.RawMaterials;

/// <summary>
/// The Raw Materials screen: ingredients and packaging in one list. A material's type decides which parts of its details
/// apply — an ingredient is measured in grams and can be made from a sub-assembly or linked to another, a packaging material
/// has a base unit of its own and is always purchased. Its details, alternative units, brand prices and a made ingredient's
/// sub-assembly and add-ins are all edited in memory, and Save writes them together. Its parts each hold a piece of it: the
/// list (<see cref="List"/>), the Cost Setup tab (<see cref="PurchaseCost"/>), the Alternative Units tab (<see cref="Units"/>),
/// a made ingredient's recipe (<see cref="Recipe"/>), the Usage Example, Price History and Dependents tabs. The screen keeps
/// the form's own fields, which material it shows, loading, Save, and the undo history over them all.
/// </summary>
public sealed class RawMaterialsState : RecordScreenState<RawMaterialFormSnapshot>, IDisposable, IRecordScreen,
    IRawMaterialsListHost, IRawMaterialUnitsHost, IRawMaterialPurchaseCostHost, IRawMaterialRecipeHost, IRawMaterialPriceLogHost
{
    /// <summary>The details tabs along the bottom: Cost Setup, Alternative Units, Usage Example, Dependents, then Price History.</summary>
    public const int PriceHistoryTab = 4;

    /// <summary>Dependents: the products the material goes into, with what it makes up of each one's material cost.</summary>
    public const int DependentsTab = 3;

    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IRawMaterialBrandPriceRepository _brandPriceRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IUnitOfMeasureRepository _unitOfMeasureRepository;
    private readonly ICostingDefaultsRepository _costingDefaultsRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly IValidator<RawMaterial> _rawMaterialValidator;
    private readonly SourceCache<RawMaterial, int> _rawMaterialCache = new(m => m.Id);
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterial> _rawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _ingredients;
    private readonly ReadOnlyObservableCollection<RawMaterial> _linkTargets;
    private readonly ReadOnlyObservableCollection<RawMaterial> _addInOptions;
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categoryOptions;

    /// <summary>The screen's derived updates, run as their inputs change, or once at the end of an undo's restore.</summary>
    private readonly DerivedUpdates _updates = new();

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

    // What a new material's Cost Buffer % starts at (Settings → Costing Defaults), as of the last LoadCommand.
    private decimal _defaultCostBufferPercent = CostingDefaults.DefaultCostBufferPercent;

    /// <summary>The material whose price history, brand prices and BOM the tables hold; null for none (a new material).</summary>
    private int? _detailMaterialId;

    private int _editingId;

    public RawMaterialsState(
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IRawMaterialBrandPriceRepository brandPriceRepository,
        IIngredientBomLineRepository bomLineRepository,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository subAssemblyLineRepository,
        IRawMaterialCategoryRepository categoryRepository,
        IValidator<RawMaterialCategory> categoryValidator,
        IValidator<RawMaterial> rawMaterialValidator,
        IValidator<RawMaterialUnit> unitValidator,
        IValidator<RawMaterialBrandPrice> brandPriceValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        IViewStateStore viewStateStore,
        ICostingDefaultsRepository costingDefaultsRepository,
        ILogger logger,
        MaterialCostShares? costShares = null)
        : base(logger)
    {
        _categoryPicker = new CategoryPicker<RawMaterialCategory>(
            () => EditingCategory, category => AsUndoStep(() => EditingCategory = category), () => CategoryOptions, c => c.Name,
            () => CategorySearchText, text => CategorySearchText = text, async typed => await PromptForNewCategory.Handle(typed),
            SaveNewCategoryAsync, RunSafelyAsync, message => SuccessMessage = message, ex => ShowFailure(ex));
        _rawMaterialRepository = rawMaterialRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _brandPriceRepository = brandPriceRepository;
        _bomLineRepository = bomLineRepository;
        _subAssemblyRepository = subAssemblyRepository;
        _categoryRepository = categoryRepository;
        _unitOfMeasureRepository = unitOfMeasureRepository;
        _costingDefaultsRepository = costingDefaultsRepository;
        _categoryValidator = categoryValidator;
        _rawMaterialValidator = rawMaterialValidator;

        // The lists the form and its parts read, bound first: the parts below take them. The categories before the list
        // part's own subscriptions to them, so its folders read them changed.
        var byName = SortExpressionComparer<RawMaterial>.Ascending(m => m.Name);
        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();
        _rawMaterialCache.Connect()
            .SortAndBind(out _rawMaterials, byName)
            .Subscribe();
        _rawMaterialCache.Connect()
            .Filter(m => m.Type == RawMaterialType.Ingredient)
            .SortAndBind(out _ingredients, byName)
            .Subscribe();

        // The parts. Each makes its commands, and subscribes to what it holds alone; what reaches from one part to another,
        // or to the form, is subscribed below, in the order it always ran.
        List = new RawMaterialsList(
            this, _rawMaterialCache, _categoryCache, RawMaterials, Categories, rawMaterialRepository, categoryRepository, categoryValidator, viewStateStore);
        Units = new RawMaterialUnits(this, UnitsOfMeasure, unitValidator);
        PurchaseCost = new RawMaterialPurchaseCost(this, Units, UnitsOfMeasure, brandPriceValidator, _updates);
        Recipe = new RawMaterialRecipe(this, Ingredients, subAssemblyLineRepository);
        UsageExample = new RawMaterialUsageExample();
        PriceLog = new RawMaterialPriceLog(this, priceHistoryRepository);
        Dependents = new RawMaterialDependents(costShares);

        // What follows the form's fields as they change is called by their setters (SelectionChanged, TypeChanged and the
        // rest), in the order it has to run. What follows here runs once now, as the screen starts, in the order it always
        // did: several of the subscriptions run as they're made, the picks' included, so each comes after what it reads.
        List.SyncTreeSelection();

        // Shared by every picker that must leave out the ingredient being edited: Linked's target and an add-in.
        var notTheSelection = _selectedIds
            .DistinctUntilChanged()
            .Select(selectedId => (Func<RawMaterial, bool>)(m => m.Type == RawMaterialType.Ingredient && m.Id != selectedId));
        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _linkTargets, byName)
            .Subscribe();
        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _addInOptions, byName)
            .Subscribe();

        // The picker offers only the categories of the material's own type, which is all it can be saved in: a made
        // ingredient's are the Made ones (RawMaterialRules.CategoryTypeFor).
        _categoryCache.Connect()
            .Filter(_categoryTypes.Select(type => (Func<RawMaterialCategory, bool>)(c => c.Type == type)))
            .SortAndBind(out _categoryOptions, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

        // Save offers itself only when the form holds something the saved material doesn't. Measured on every change rather
        // than only when an undo step settles: a step closes on the way out of a field, and a Save button still disabled
        // under the cursor would swallow the very click that enabled it. A table's add row is the same story the other way
        // round - the table raises nothing when it commits, so the step stays open. The parts that hold the form's own
        // fields too: the Cost Setup's buffer, the units, and a made ingredient's recipe. What a field's change sets off is
        // measured again once it has run (SelectionChanged, TypeChanged and the rest).
        PropertyChanged += (_, _) => History.RefreshDirty();
        PurchaseCost.PropertyChanged += (_, _) => History.RefreshDirty();
        Units.PropertyChanged += (_, _) => History.RefreshDirty();
        Recipe.PropertyChanged += (_, _) => History.RefreshDirty();
        WatchForSave(Units.Rows);
        WatchForSave(PurchaseCost.BrandPriceRows);
        WatchForSave(Recipe.AddIns);

        // The header's saved costs; a new Per unit, or a unit's multiplier, updates them through UpdatePurchaseCostTexts.
        PurchaseCost.UpdateHeaderCostTexts();

        // The summary follows the brand table as it's typed into: the dearest brand, as Save will cost the material.
        PurchaseCost.BrandPriceRows.ToObservableChangeSet()
            .AutoRefresh(r => r.Price)
            .AutoRefresh(r => r.Quantity)
            .AutoRefresh(r => r.Brand)
            .Subscribe(_ => _updates.Run(PurchaseCost.UpdatePurchaseCostTexts));

        // Each brand's buffered price follows the fixed buffered cost (the Cost Setup's BufferEdited), and a made
        // ingredient's totals the Weight Loss % box, which moves the yield the cost is spread over.
        _updates.Run(PurchaseCost.BufferedCostChanged);
        _updates.Run(Recipe.RecomputeBomTotals);

        // The Recipe card shows the picked sub-assembly's lines as it's saved: read again on every pick, an undo's included,
        // and on every load, which re-points the pick at the refreshed copy, so a change made on the Sub-Assemblies screen
        // shows here. The totals follow them.
        LoadLatest(Recipe.SubAssemblies, Recipe.LoadLinesAsync);

        // The alternative units and the purchase unit choices name the base unit (TypeChanged, BaseUnitChanged).
        RebuildUnitViews();

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

        // A unit's multiplier changed in the Alternative Units table reprices the brands bought in it straight away, and the
        // add row goes once there are two units.
        Units.Rows.ToObservableChangeSet()
            .AutoRefresh(r => r.UnitOfMeasureId)
            .AutoRefresh(r => r.BaseUnitsPerUnit)
            .Where(_ => !Units.IsSyncing && !IsBusyApplying)
            .Subscribe(_ => RepriceBrands());

        // An ingredient picked in the Add-Ins table is pointed at the screen's copy of it, for its name; and the header's
        // Total Weight and Total Cost follow the add-ins as they're typed into: Cost covers both an ingredient picked and
        // grams typed, as the sub-assembly form's batch totals do.
        Recipe.AddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => _updates.Run(Recipe.ResolveAddIns));
        Recipe.AddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.Cost)
            .Subscribe(_ => _updates.Run(Recipe.RecomputeBomTotals));

        // The commands. Making one runs nothing.

        // Runs each time the page opens (ScreenBase) and after this screen's own saves. It merges rather than replaces, so
        // the screen keeps its selection, picker choices and unsaved edits while picking up what changed elsewhere, such as
        // a unit renamed in Settings.
        LoadCommand = new ScreenCommand(() => LoadSafelyAsync(LoadAsync));

        // A new material replaces the one showing, so its unsaved changes are asked about first.
        NewCommand = new ScreenCommand(() => AfterUnsavedChangesAsync(() => List.NewInList()));
        SaveCommand = new ScreenCommand(() => RunSaveAsync(SaveAsync), () => History.IsDirty);
        DeleteCommand = new ScreenCommand(() => DeleteRawMaterialAsync(SelectedRawMaterial!), () => SelectedRawMaterial is not null);
    }

    /// <summary>The list: the materials by type and category, searched, the rows picked, their menus, moves and naming in place.</summary>
    public RawMaterialsList List { get; }

    /// <summary>The Cost Setup tab: the brand prices, the buffered cost and its target, and the header's saved costs.</summary>
    public RawMaterialPurchaseCost PurchaseCost { get; }

    /// <summary>The Alternative Units tab: the units the material is bought or counted in, and how many base units each holds.</summary>
    public RawMaterialUnits Units { get; }

    /// <summary>A made ingredient's recipe: the sub-assembly it's made from, its lines, the add-ins, and the batch's totals.</summary>
    public RawMaterialRecipe Recipe { get; }

    /// <summary>The Usage Example tab: what a quantity of a material costs.</summary>
    public RawMaterialUsageExample UsageExample { get; }

    /// <summary>The Price History tab: a purchased material's price changes.</summary>
    public RawMaterialPriceLog PriceLog { get; }

    /// <summary>The Dependents tab: the products the material goes into.</summary>
    public RawMaterialDependents Dependents { get; }

    /// <summary>Every raw material, for the usage example's picker.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _rawMaterials;

    /// <summary>
    /// The ingredients only: packaging never goes in a recipe. Resolves a BOM line's IngredientId and a saved LinkedToId back
    /// to their material; a picker that shouldn't offer the material being edited uses AddInOptions or LinkTargets instead.
    /// </summary>
    public ReadOnlyObservableCollection<RawMaterial> Ingredients => _ingredients;

    /// <summary>The ingredients a linked ingredient can be linked to: every one but the material being edited.</summary>
    public ReadOnlyObservableCollection<RawMaterial> LinkTargets => _linkTargets;

    /// <summary>The Add-Ins table's ingredient picker's list: every ingredient but the made one being edited — a made ingredient can't be its own add-in.</summary>
    public ReadOnlyObservableCollection<RawMaterial> AddInOptions => _addInOptions;

    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    /// <summary>
    /// What the Category picker lists: the categories of the material's type. A name that isn't one of them is added by
    /// typing it (CommitCategoryTextAsync).
    /// </summary>
    public ReadOnlyObservableCollection<RawMaterialCategory> CategoryOptions => _categoryOptions;

    /// <summary>Every unit of measure, for the base unit picker and the Alternative Units table's unit cell.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

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
                SelectionChanged();
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
        // A material opened, by its Save included, ends the new one's row.
        if (SelectedRawMaterial is not null)
        {
            List.EndNewRow();
        }

        List.SyncTreeSelection();
        _selectedIds.OnNext(SelectedRawMaterial?.Id ?? 0);

        // A hidden tab can't stay the one showing: picking a made or new material while on Price History goes back to Cost
        // Setup.
        if (SelectedRawMaterial is not { Source: RawMaterialSource.Purchased } && SelectedDetailsTab == PriceHistoryTab)
        {
            SelectedDetailsTab = 0;
        }

        PurchaseCost.UpdateHeaderCostTexts();
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

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => RaiseAndSetIfChanged(ref _editingName, value);
    }

    private RawMaterialType _editingType = RawMaterialType.Ingredient;

    /// <summary>The form's type, as loaded, put back or picked (<see cref="ChangeType"/>).</summary>
    public RawMaterialType EditingType
    {
        get => _editingType;
        private set
        {
            if (RaiseAndSetIfChanged(ref _editingType, value))
            {
                TypeChanged();
            }
        }
    }

    /// <summary>The user picks the type, on a new material: what the other type had filled in goes (TypeEdited).</summary>
    public void ChangeType(RawMaterialType type)
    {
        if (type != EditingType)
        {
            EditingType = type;
            TypeEdited(type);
            BaseUnitEdited();
        }
    }

    /// <summary>
    /// What follows the form's type, set by the user or shown: the categories it can go in, and the alternative units and
    /// purchase unit choices, which name its base unit.
    /// </summary>
    private void TypeChanged()
    {
        CategoryTypeChanged();
        RebuildUnitViews();
        History.RefreshDirty();
    }

    /// <summary>
    /// The form's type or source changed: the Category picker offers that kind's categories. A new material's category of
    /// another kind is no longer one it can go in, so it goes. Only a new one's: a saved material's are fixed, and loading
    /// one sets its category before its source.
    /// </summary>
    private void CategoryTypeChanged()
    {
        var type = RawMaterialRules.CategoryTypeFor(EditingType, EditingSource);
        _categoryTypes.OnNext(type);
        if (CanChangeTypeAndSource && EditingCategory is { } category && category.Type != type)
        {
            EditingCategory = null;
        }
    }

    private RawMaterialCategory? _editingCategory;
    public RawMaterialCategory? EditingCategory
    {
        get => _editingCategory;
        set => RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    private RawMaterialSource _editingSource = RawMaterialSource.Purchased;

    /// <summary>The form's source, as loaded, put back or picked (<see cref="ChangeSource"/>).</summary>
    public RawMaterialSource EditingSource
    {
        get => _editingSource;
        private set
        {
            if (RaiseAndSetIfChanged(ref _editingSource, value))
            {
                SourceChanged();
            }
        }
    }

    /// <summary>The user picks the source, on a new material.</summary>
    public void ChangeSource(RawMaterialSource source)
    {
        if (source == EditingSource)
        {
            return;
        }

        EditingSource = source;

        // A made ingredient starts at 3% weight loss, since a small shrink is the normal case in the shop, and the box stays
        // freely editable — typing 0 is a legitimate custom value. Only when the box is empty, so one that holds a figure
        // keeps it.
        if (source == RawMaterialSource.Made)
        {
            EditingWeightLossPercent ??= 3m;
        }
    }

    /// <summary>What follows the form's source, set by the user or shown: the categories it can go in.</summary>
    private void SourceChanged()
    {
        CategoryTypeChanged();
        History.RefreshDirty();
    }

    private decimal? _editingWeightLossPercent;
    /// <summary>
    /// Made only: how much of the batch's weight is lost in the making, which its yield is less. A made ingredient's totals
    /// follow it, as it moves the yield the cost is spread over.
    /// </summary>
    public decimal? EditingWeightLossPercent
    {
        get => _editingWeightLossPercent;
        set
        {
            if (RaiseAndSetIfChanged(ref _editingWeightLossPercent, value))
            {
                _updates.Run(Recipe.RecomputeBomTotals);
            }
        }
    }

    private RawMaterial? _editingLinkedTo;
    /// <summary>Linked only: the ingredient this one is a part of.</summary>
    public RawMaterial? EditingLinkedTo
    {
        get => _editingLinkedTo;
        set => RaiseAndSetIfChanged(ref _editingLinkedTo, value);
    }

    private decimal? _editingWeightSharePercent;
    /// <summary>Linked only: how much of the linked-to ingredient's weight this part is.</summary>
    public decimal? EditingWeightSharePercent
    {
        get => _editingWeightSharePercent;
        set => RaiseAndSetIfChanged(ref _editingWeightSharePercent, value);
    }

    private decimal? _editingCostSharePercent;
    /// <summary>Linked only: how much of the linked-to ingredient's cost this part carries.</summary>
    public decimal? EditingCostSharePercent
    {
        get => _editingCostSharePercent;
        set => RaiseAndSetIfChanged(ref _editingCostSharePercent, value);
    }

    private UnitOfMeasure? _editingBaseUnit;

    /// <summary>
    /// Packaging only; an ingredient's base unit is always the gram. As loaded, put back or picked
    /// (<see cref="ChangeBaseUnit"/>).
    /// </summary>
    public UnitOfMeasure? EditingBaseUnit
    {
        get => _editingBaseUnit;
        private set
        {
            if (RaiseAndSetIfChanged(ref _editingBaseUnit, value))
            {
                BaseUnitChanged();
            }
        }
    }

    /// <summary>The user picks the base unit, on a new packaging material: the brand rows priced in it move onto it (BaseUnitEdited).</summary>
    public void ChangeBaseUnit(UnitOfMeasure? unit)
    {
        if (unit != EditingBaseUnit)
        {
            EditingBaseUnit = unit;
            BaseUnitEdited();
        }
    }

    /// <summary>What follows the form's base unit, set by the user or shown: the alternative units and purchase unit choices name it.</summary>
    private void BaseUnitChanged()
    {
        RebuildUnitViews();
        History.RefreshDirty();
    }

    private bool _canChangeTypeAndSource = true;
    /// <summary>Only a new material's type and source can be picked: both are fixed once saved (RawMaterialRules).</summary>
    public bool CanChangeTypeAndSource
    {
        get => _canChangeTypeAndSource;
        private set => RaiseAndSetIfChanged(ref _canChangeTypeAndSource, value);
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
    public bool IsEditingIngredient => EditingType == RawMaterialType.Ingredient;

    public bool IsEditingPackaging => EditingType == RawMaterialType.Packaging;

    /// <summary>A made ingredient: it has a sub-assembly, add-ins and a weight loss percent instead of a purchase price.</summary>
    public bool IsEditingMade => EditingType == RawMaterialType.Ingredient && EditingSource == RawMaterialSource.Made;

    /// <summary>A linked ingredient: its cost is a share of another ingredient's, instead of a price or a BOM.</summary>
    public bool IsEditingLinked => EditingType == RawMaterialType.Ingredient && EditingSource == RawMaterialSource.Linked;

    /// <summary>What the link works out to as it's filled in: the linked-to ingredient's cost per gram, and this part's.</summary>
    public string LinkedCostPreviewText => LinkedCostPreview(EditingLinkedTo, EditingWeightSharePercent, EditingCostSharePercent);

    /// <summary>The linked-to ingredient's other parts, and how much of its weight and cost all of them take with this one.</summary>
    public string LinkedPartsText => LinkedPartsSummary(EditingLinkedTo, EditingWeightSharePercent, EditingCostSharePercent);

    /// <summary>A purchased material, saved or still being filled in, so a new one can be priced before its first Save.</summary>
    public bool ShowPurchaseSetup => EditingType == RawMaterialType.Packaging || EditingSource == RawMaterialSource.Purchased;

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
    public string BaseUnitName => BaseUnitText.NameOf(EditingType, EditingBaseUnit);

    public string BaseUnitAbbreviation => BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);

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

    public ScreenCommand LoadCommand { get; }

    public ScreenCommand NewCommand { get; }

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

    /// <summary>Load: everything the screen shows is read again, and merged in.</summary>
    private async Task LoadAsync()
    {
        // All at once, on the thread pool, as Products reads its lists: one after another on the UI thread, each read came
        // back to it before the next began, and waited its turn behind whatever the screen was drawing. The products
        // using the material showing are read with them, since Products may have started or stopped using it meanwhile.
        var detailMaterialId = _detailMaterialId;
        var categoriesRead = Task.Run(() => _categoryRepository.GetAllAsync(CancellationToken.None));
        var unitsRead = Task.Run(() => _unitOfMeasureRepository.GetAllAsync(CancellationToken.None));
        var rawMaterialsRead = Task.Run(() => _rawMaterialRepository.GetAllAsync(CancellationToken.None));
        var costingDefaultsRead = Task.Run(() => _costingDefaultsRepository.GetAsync(CancellationToken.None));
        var subAssembliesRead = Task.Run(() => _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None));
        var dependentsRead = detailMaterialId is { } dependentsOf
            ? Task.Run(() => _rawMaterialRepository.GetDependentsAsync(dependentsOf, CancellationToken.None))
            : Task.FromResult<IReadOnlyList<LinkingProduct>>([]);
        await Task.WhenAll(categoriesRead, unitsRead, rawMaterialsRead, costingDefaultsRead, subAssembliesRead, dependentsRead);

        var categories = await categoriesRead;
        var units = await unitsRead;
        var rawMaterials = await rawMaterialsRead;
        var costingDefaults = await costingDefaultsRead;
        var subAssemblies = await subAssembliesRead;

        // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
        var selectedId = SelectedRawMaterial?.Id;
        var categoryId = EditingCategory?.Id;
        var linkedToId = EditingLinkedTo?.Id;
        var subAssemblyId = Recipe.SubAssembly?.Id;
        var baseUnitId = EditingBaseUnit?.Id;
        var exampleMaterialId = UsageExample.Material?.Id;
        var exampleUnit = UsageExample.Unit;

        ApplyRefresh(() =>
        {
            _categoryCache.MergeFrom(categories);
            UnitsOfMeasure.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
            _rawMaterialCache.MergeFrom(rawMaterials);

            EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
            EditingLinkedTo = Ingredients.FirstOrDefault(m => m.Id == linkedToId);

            // A packaging set is never a recipe to make an ingredient from.
            Recipe.TakeOptions(subAssemblies);
            Recipe.SubAssembly = Recipe.SubAssemblyOptions.FirstOrDefault(s => s.Id == subAssemblyId);
            var previousBaseUnit = EditingBaseUnit;
            EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId);

            Recipe.ResolveAddIns();

            // Resolve points each row at the refreshed component, so this is what carries a component repriced
            // elsewhere into the header's totals: the change set above is suppressed while a refresh applies.
            Recipe.RecomputeBomTotals();

            SelectedRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == selectedId);

            // The form's alternative units may not be saved yet, so they stay rather than being reloaded from the refreshed
            // material. Each points at its refreshed unit of measure, so one renamed in Settings shows its new name.
            Units.RefreshUnitsOfMeasure();

            if (!ReferenceEquals(previousBaseUnit, EditingBaseUnit))
            {
                Units.UpdateBaseUnit();
            }

            // Every time, so a unit of measure created or renamed in Settings shows as a purchase unit.
            PurchaseCost.RebuildPurchaseUnitOptions();

            RefreshDefaultCostBuffer(costingDefaults.CostBufferPercent);

            UsageExample.Refresh(RawMaterials.FirstOrDefault(m => m.Id == exampleMaterialId), exampleUnit);
        });

        // The Sub-Assemblies screen may have changed the recipe's lines. Pointing the recipe at its fresh copy reads them only
        // when the sub-assembly's own row changed, so they're read here every time.
        if (Recipe.SubAssembly is { } recipe)
        {
            await Recipe.LoadLinesAsync(recipe, CancellationToken.None);
        }

        // The products using the material, read with the lists: kept only if the details showing are still that material's.
        if (detailMaterialId is { } readFor && _detailMaterialId == readFor)
        {
            Dependents.Show(await dependentsRead);
        }

        // After the merge, which only points the selection at its fresh copy: this one has to load the material's form.
        OpenRecordAskedFor();
    }

    /// <summary>Save: the material with its units, brand prices or add-ins, all or nothing.</summary>
    private async Task SaveAsync()
    {
        // The picked material's details haven't replaced the previous one's yet: saving now would write that material's
        // brand prices or BOM under this one. The moment passes in a fraction of a second.
        if (!AreDetailsReady)
        {
            return;
        }

        var type = EditingType;
        var source = type == RawMaterialType.Packaging ? RawMaterialSource.Purchased : EditingSource;
        var material = new RawMaterial
        {
            Id = _editingId,
            Name = EditingName.Trim(),
            Type = type,
            CategoryId = EditingCategory?.Id ?? 0,
            Source = source,
            // A cleared box shows 0.00, so it saves as 0%.
            WeightLossPercent = source == RawMaterialSource.Made ? EditingWeightLossPercent ?? 0m : null,
            SubAssemblyId = source == RawMaterialSource.Made ? Recipe.SubAssembly?.Id : null,
            LinkedToId = source == RawMaterialSource.Linked ? EditingLinkedTo?.Id : null,
            WeightSharePercent = source == RawMaterialSource.Linked ? EditingWeightSharePercent : null,
            CostSharePercent = source == RawMaterialSource.Linked ? EditingCostSharePercent : null,
            BaseUnitId = type == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null,
            // A cleared box shows 0.00 too. A linked ingredient inherits the buffer of the one it's linked to
            // (the repository sets it from the saved one either way).
            CostBufferPercent = source == RawMaterialSource.Linked
                ? EditingLinkedTo?.CostBufferPercent ?? 0m
                : PurchaseCost.EditingCostBufferPercent ?? 0m,
            TargetCostBufferPercent = source == RawMaterialSource.Purchased ? PurchaseCost.EditingTargetCostBufferPercent ?? 0m : null,
            PreferredBrand = source == RawMaterialSource.Purchased ? PurchaseCost.EditingPreferredBrand : null,
            // Only a purchased material's is the form's; a made or linked one's is worked out by the cascade.
            BufferedCostPerBaseUnit = source == RawMaterialSource.Purchased ? PurchaseCost.EditingBufferedCost ?? 0m : 0m,
        };

        var validation = _rawMaterialValidator.Validate(material);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ToString(" ");
            return;
        }

        // The units and brand prices are typed straight into their tables, so a half-filled or repeated one is caught here.
        Units.EnsureValid();
        var brandPrices = source == RawMaterialSource.Purchased ? PurchaseCost.CompleteBrandPrices() : null;

        // The picker already leaves the material being edited out of AddInOptions, so this can't be picked through
        // the UI; caught here with a plainer message than the repository's cycle check gives.
        if (source == RawMaterialSource.Made && Recipe.AddIns.Any(l => l.IngredientId == _editingId))
        {
            ErrorMessage = "An ingredient can't be an add-in of itself.";
            return;
        }

        var addIns = source == RawMaterialSource.Made ? Recipe.ChangedAddIns() : null;

        var id = await _rawMaterialRepository.SaveAsync(material, [.. Units.Rows.Select(r => r.Unit)], brandPrices, addIns, CancellationToken.None);

        // The list picks up any cost that cascaded to other materials; then the form reloads what
        // was saved, so new units get their ids and the brand prices, price history and BOM show the new rows.
        await LoadCommand.ExecuteAsync();
        await ReloadSavedRawMaterialAsync(id);
        SuccessMessage = $"{EditingName} was saved.";
    }

    /// <summary>
    /// New, from the header or the list. A material still waiting for LoadCommand to open it is no longer opened: it would
    /// load over the new form, and a name typed into it since would then be saved as that material's.
    /// </summary>
    private void StartNewMaterial(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        ForgetRecordToOpen();
        ClearForm(type, categoryId, source);
    }

    /// <summary>
    /// Copy, from the list: a new material of <paramref name="material"/>'s type, in its category, with its base unit,
    /// named "… Copy" ("… Copy 2" when that's taken). A made ingredient's copy is a made one, as its category holds only
    /// those; a linked one's is purchased. The name and the base unit come after the form is settled, so the copy is
    /// there to be saved, and leaving it asks first.
    /// </summary>
    private void StartCopyOf(RawMaterial material)
    {
        StartNewMaterial(
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
    /// An empty form, for New and after Delete. It starts as a purchased ingredient, the usual kind, but the category, brand
    /// prices and the usage example's material start empty, so none is saved or costed by accident. The numbers keep a
    /// value: the cost buffer is Settings' default, and the usage example's quantity stays. The list's New … Here starts it
    /// as <paramref name="type"/> in <paramref name="categoryId"/> instead, and as a made ingredient
    /// (<paramref name="source"/>) in a Made category, set before the form is settled, so it isn't taken for an edit to save.
    /// </summary>
    private void ClearForm(
        RawMaterialType type = RawMaterialType.Ingredient, int? categoryId = null, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        // The new material's row, if one was named in the list, goes with the form it stood for.
        List.EndNewRow();
        History.Clear();
        FormVersion++;
        _editingId = 0;
        Pick(null);
        CanChangeTypeAndSource = true;
        EditingName = string.Empty;

        // Set as the user sets them, so the form is a new material of that kind, with nothing of another kind's left in it.
        ChangeType(type);
        // Emptied before the source is set, which gives a made one its starting weight loss.
        EditingWeightLossPercent = null;
        ChangeSource(source);
        EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId && c.Type == RawMaterialRules.CategoryTypeFor(type, source));

        Recipe.IsChangingSubAssembly = false;
        Recipe.SubAssembly = null;
        EditingLinkedTo = null;
        EditingWeightSharePercent = null;
        EditingCostSharePercent = null;
        PurchaseCost.EditingCostBufferPercent = _defaultCostBufferPercent;
        PurchaseCost.EditingTargetCostBufferPercent = _defaultCostBufferPercent;
        PurchaseCost.EditingPreferredBrand = null;
        PurchaseCost.EditingBufferedCost = null;
        ChangeBaseUnit(null);
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

        _editingId = material.Id;
        CanChangeTypeAndSource = false;
        EditingName = material.Name;
        EditingType = material.Type;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == material.CategoryId);
        EditingSource = material.Source;
        EditingWeightLossPercent = material.WeightLossPercent;
        Recipe.IsChangingSubAssembly = false;
        Recipe.SubAssembly = Recipe.SubAssemblyOptions.FirstOrDefault(s => s.Id == material.SubAssemblyId);
        EditingLinkedTo = Ingredients.FirstOrDefault(m => m.Id == material.LinkedToId);
        EditingWeightSharePercent = material.WeightSharePercent;
        EditingCostSharePercent = material.CostSharePercent;
        PurchaseCost.EditingCostBufferPercent = material.CostBufferPercent;
        PurchaseCost.EditingTargetCostBufferPercent = RawMaterialRules.TargetBufferPercentOf(material);
        PurchaseCost.EditingPreferredBrand = material.PreferredBrand;
        PurchaseCost.EditingBufferedCost = material.BufferedCostPerBaseUnit;
        EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId);
    }

    /// <summary>
    /// The type changed by the user, on a new material: a packaging material is always purchased, and an ingredient's base
    /// unit is the gram, so whatever the other type had filled in goes.
    /// </summary>
    private void TypeEdited(RawMaterialType type)
    {
        if (type == RawMaterialType.Packaging)
        {
            ChangeSource(RawMaterialSource.Purchased);
            EditingWeightLossPercent = null;
            EditingLinkedTo = null;
            EditingWeightSharePercent = null;
            EditingCostSharePercent = null;
        }
        else
        {
            ChangeBaseUnit(null);
        }
    }

    /// <summary>The base unit changed by the user, on a new packaging material: the brand rows priced in it move onto it.</summary>
    private void BaseUnitEdited()
    {
        PurchaseCost.MoveBrandRowsOntoTheBaseUnit();
        RebuildUnitViews();
    }

    /// <summary>Shows the base unit on the form's alternative units and in the purchase unit choices.</summary>
    private void RebuildUnitViews()
    {
        Units.UpdateBaseUnit();
        PurchaseCost.RebuildPurchaseUnitOptions();
    }

    /// <summary>A unit's multiplier changed, or a unit went: the brands bought in it are priced again.</summary>
    private void RepriceBrands()
    {
        PurchaseCost.RefreshBrandPriceRows();
        PurchaseCost.UpdatePurchaseCostTexts();
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
            detail = await Task.Run(() => ReadDetailAsync(material, cancellationToken), cancellationToken);
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

    /// <summary>A material's price history, brand prices, BOM lines and the products using it, as LoadDetailsAsync reads them.</summary>
    private sealed record RawMaterialDetail(
        IReadOnlyList<RawMaterialPriceHistory> PriceHistory,
        IReadOnlyList<RawMaterialBrandPrice> BrandPrices,
        IReadOnlyList<IngredientBomLine> BomLines,
        IReadOnlyList<LinkingProduct> Dependents)
    {
        public static readonly RawMaterialDetail None = new([], [], [], []);
    }

    /// <summary>
    /// Only the repositories: this runs off the UI thread. A purchased material has prices, a made one a BOM, and a linked
    /// one neither; any of them can be used by products.
    /// </summary>
    private async Task<RawMaterialDetail> ReadDetailAsync(RawMaterial material, CancellationToken cancellationToken)
    {
        var dependents = await _rawMaterialRepository.GetDependentsAsync(material.Id, cancellationToken);
        return material.Source switch
        {
            RawMaterialSource.Purchased => new RawMaterialDetail(
                await _priceHistoryRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                await _brandPriceRepository.GetForRawMaterialAsync(material.Id, cancellationToken),
                [],
                dependents),
            RawMaterialSource.Made => new RawMaterialDetail([], [], await _bomLineRepository.GetForIngredientAsync(material.Id, cancellationToken), dependents),
            _ => RawMaterialDetail.None with { Dependents = dependents },
        };
    }

    /// <summary>
    /// Puts <paramref name="material"/>'s details in place of whatever the tables held, in one pass, so nothing is drawn in
    /// between, along with what Save compares the BOM against. None for no material.
    /// </summary>
    private void ShowDetails(RawMaterial? material, RawMaterialDetail detail)
    {
        PriceLog.Show(material, detail.PriceHistory);
        ShowUpdatedAt(material);

        // The recipe's own lines show from the sub-assembly picked (Recipe.LoadLinesAsync); only the add-ins are the form's.
        Recipe.ShowAddIns(detail.BomLines);
        PurchaseCost.ShowBrandPrices(detail.BrandPrices);
        Dependents.Show(detail.Dependents);

        UpdateCanChangeBaseUnit();
        PurchaseCost.RebuildBufferedCostUnitOptions();
        PurchaseCost.BufferedCostUnit = PurchaseCost.DearestBrandUnit();
        _detailMaterialId = material?.Id;
        AreDetailsReady = true;
        Recipe.RecomputeBomTotals();

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
    /// Re-fetches the material just saved and pushes it into the list's cache and the selection. A fresh copy is always a
    /// new selection, so the form and its details reload from what was saved. A new material, saved for the first time, is
    /// still the one whose details show, not another material: they stay until the reload replaces them, as an existing
    /// one's do.
    /// </summary>
    private async Task ReloadSavedRawMaterialAsync(int id)
    {
        var saved = await _rawMaterialRepository.GetByIdAsync(id, CancellationToken.None);
        if (saved is not null)
        {
            _rawMaterialCache.AddOrUpdate(saved);
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
        var category = new RawMaterialCategory { Name = name, Type = RawMaterialRules.CategoryTypeFor(EditingType, EditingSource) };
        var validation = _categoryValidator.Validate(category);
        if (!validation.IsValid)
        {
            ErrorMessage = validation.ToString(" ");
            return null;
        }

        // AddAsync fills in the saved Id, which the reloaded copy to pick is found by.
        await _categoryRepository.AddAsync(category, CancellationToken.None);
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

        if (!List.VisibleRawMaterials.Contains(material))
        {
            List.SearchText = string.Empty;
        }

        Pick(material);
        return true;
    }

    /// <summary>
    /// Deletes <paramref name="material"/> once it's confirmed: the header's Delete, and the list's. The form empties if it
    /// was showing it.
    /// </summary>
    private Task DeleteRawMaterialAsync(RawMaterial material) =>
        RunWithConfirmationAsync($"Delete '{material.Name}'? This can't be undone.", async () =>
        {
            await _rawMaterialRepository.DeleteAsync(material.Id, CancellationToken.None);
            if (SelectedRawMaterial?.Id == material.Id || _editingId == material.Id)
            {
                ClearForm();
            }

            await LoadCommand.ExecuteAsync();
        });

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

    protected override RawMaterialFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        Type = EditingType,
        CategoryId = EditingCategory?.Id,
        Source = EditingSource,
        CanChangeTypeAndSource = CanChangeTypeAndSource,
        BaseUnitId = EditingBaseUnit?.Id,
        WeightLossPercent = EditingWeightLossPercent,
        SubAssemblyId = Recipe.SubAssembly?.Id,
        LinkedToId = EditingLinkedTo?.Id,
        WeightSharePercent = EditingWeightSharePercent,
        CostSharePercent = EditingCostSharePercent,
        CostBufferPercent = PurchaseCost.EditingCostBufferPercent,
        TargetCostBufferPercent = PurchaseCost.EditingTargetCostBufferPercent,
        PreferredBrand = PurchaseCost.EditingPreferredBrand,
        BufferedCost = PurchaseCost.EditingBufferedCost,
        BufferedCostUnitKey = PurchaseCost.BufferedCostUnit?.Key,
        UnitRows = Units.Capture(),
        BrandPrices = PurchaseCost.CaptureBrandPrices(),
        AddIns = Recipe.CaptureAddIns(),
    };

    /// <summary>
    /// Puts the form back as <paramref name="snapshot"/> had it. The rows are rebuilt rather than written into: an
    /// IngredientLineRow's setters drop a typed percentage, and a unit row writes straight through to the stored
    /// RawMaterialUnit that Save hands the repository, so each unit comes back as a fresh entity carrying its own
    /// Id (RawMaterialUnits.Restore). The one recompute runs at the end, outside the guard, so the costs and captions are
    /// worked out once from the form that was put back.
    /// </summary>
    protected override void RestoreSnapshot(RawMaterialFormSnapshot snapshot, RawMaterialFormSnapshot? settledThen)
    {
        ErrorMessage = null;

        ApplyRestore(() => _updates.Batch(() =>
        {
            Units.SelectedRow = null;
            PurchaseCost.SelectedBrandPriceRow = null;
            Recipe.SelectedAddIn = null;

            _editingId = snapshot.EditingId;
            CanChangeTypeAndSource = snapshot.CanChangeTypeAndSource;
            EditingName = snapshot.Name;
            EditingType = snapshot.Type;
            EditingCategory = snapshot.CategoryId is { } categoryId ? Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            EditingSource = snapshot.Source;
            EditingBaseUnit = snapshot.BaseUnitId is { } baseUnitId ? UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId) : null;
            EditingWeightLossPercent = snapshot.WeightLossPercent;
            Recipe.IsChangingSubAssembly = false;
            Recipe.SubAssembly = snapshot.SubAssemblyId is { } subAssemblyId ? Recipe.SubAssemblyOptions.FirstOrDefault(s => s.Id == subAssemblyId) : null;
            EditingLinkedTo = snapshot.LinkedToId is { } linkedToId ? Ingredients.FirstOrDefault(m => m.Id == linkedToId) : null;
            EditingWeightSharePercent = snapshot.WeightSharePercent;
            EditingCostSharePercent = snapshot.CostSharePercent;
            PurchaseCost.EditingCostBufferPercent = snapshot.CostBufferPercent;
            PurchaseCost.EditingTargetCostBufferPercent = snapshot.TargetCostBufferPercent;
            PurchaseCost.EditingPreferredBrand = snapshot.PreferredBrand;
            PurchaseCost.EditingBufferedCost = snapshot.BufferedCost;

            Units.Restore(snapshot.UnitRows);
            PurchaseCost.RestoreBrandPrices(snapshot.BrandPrices);
            Recipe.RestoreAddIns(snapshot.AddIns);
        }));

        // The Per picker's choices are rebuilt from the units above, so the one that was picked is found again here
        // rather than in the guard: RebuildUnitViews replaces the list BufferedCostUnit is chosen from.
        RebuildUnitViews();
        PurchaseCost.BufferedCostUnit = snapshot.BufferedCostUnitKey is { } unitKey
            ? PurchaseCost.BufferedCostUnitOptions.FirstOrDefault(o => o.Key == unitKey)
            : null;

        Recipe.ResolveAddIns();
        PurchaseCost.RefreshBrandPriceRows();
        PurchaseCost.UpdatePurchaseCostTexts();

        // The Cost Buffer % box shows TypedCostBufferPercent, raised as the buffer changes: an undo that leaves the buffer
        // as it was doesn't, and without this the box would keep the percent from before the undo (undoing a
        // Basis pick, say).
        PurchaseCost.RaiseTypedCostBufferPercent();
        Recipe.RecomputeBomTotals();
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

    /// <summary>
    /// Don't Save on leaving the screen: the form goes back to the material as it was last saved, read again, since its
    /// alternative units write through to the copy the list holds. A new material was never saved, so it goes back to a
    /// blank one.
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

    // What the list asks of the screen (IRawMaterialsListHost): the material the form shows, and what the list's actions do to it.
    int IRawMaterialsListHost.EditingId => _editingId;

    Task IRawMaterialsListHost.LoadAsync() => LoadCommand.ExecuteAsync();

    void IRawMaterialsListHost.Pick(RawMaterial material) =>
        PickFromList(material.Id, () => Pick(material), List.SyncTreeSelection);

    void IRawMaterialsListHost.StartNew(RawMaterialType type, int? categoryId, RawMaterialSource source) => StartNewMaterial(type, categoryId, source);

    void IRawMaterialsListHost.StartCopyOf(RawMaterial material) => StartCopyOf(material);

    Task IRawMaterialsListHost.DeleteAsync(RawMaterial material) => DeleteRawMaterialAsync(material);

    void IRawMaterialsListHost.NameNew(string name) => AsUndoStep(() => EditingName = name);

    void IRawMaterialsListHost.ClearNewName() => EditingName = string.Empty;

    void IRawMaterialsListHost.RenamedOnForm(string name)
    {
        EditingName = name;
        History.RewriteAll(snapshot => snapshot with { Name = name });
    }

    void IRawMaterialsListHost.MovedOnForm(int categoryId)
    {
        EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
        History.RewriteAll(snapshot => snapshot with { CategoryId = categoryId });
    }

    void IRawMaterialsListHost.CategoryDeleted(int categoryId) =>
        History.RewriteAll(snapshot => snapshot.CategoryId == categoryId ? snapshot with { CategoryId = null } : snapshot);

    // What the Alternative Units tab, the Cost Setup tab, a made ingredient's recipe and the Price History tab ask of it.
    void IRawMaterialUnitsHost.UnitsChanged() => RepriceBrands();

    decimal IRawMaterialPurchaseCostHost.BomTotalWeight => Recipe.BomTotalWeight;

    decimal IRawMaterialPurchaseCostHost.BomTotalCost => Recipe.BomTotalCost;

    decimal? IRawMaterialRecipeHost.WeightLossPercent => EditingWeightLossPercent;

    void IRawMaterialRecipeHost.BomTotalsChanged() => PurchaseCost.UpdateHeaderCostTexts();

    void IRawMaterialPriceLogHost.PricesDeleted() => UpdateCanChangeBaseUnit();

    public void Dispose()
    {
        // A change still settling when the app closes is written now, rather than lost.
        List.Dispose();

        _rawMaterialCache.Dispose();
        _categoryCache.Dispose();
        Dependents.Dispose();
        Recipe.Dispose();
        _picks.Dispose();
        _selectedIds.Dispose();
        _categoryTypes.Dispose();
        _detailsTabs.Dispose();
    }
}
