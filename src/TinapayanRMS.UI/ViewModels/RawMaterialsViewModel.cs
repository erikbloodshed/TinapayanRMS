using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row in the Alternative Units grid and a box in the Unit Conversion Overview: one of the form's
/// alternative units, already saved (its Unit has an Id) or not yet (Id 0). Typed straight into: the grid's
/// unit cell sets <see cref="UnitOfMeasureId"/>, which the row resolves to its unit of measure, and the multiplier
/// cell sets <see cref="BaseUnitsPerUnit"/>. Both write through to <see cref="Unit"/>, which Save writes. A row can be
/// half filled in; Save checks them all first.
/// </summary>
public sealed class RawMaterialUnitRow : ReactiveObject
{
    /// <summary>Looks up a unit of measure by id as the screen has it.</summary>
    private readonly Func<int, UnitOfMeasure?>? _findUnitOfMeasure;

    /// <summary>An empty row, for the grid to compare an add row against (controls:GridAddNewRow.DropWhenUntouched).</summary>
    public RawMaterialUnitRow()
        : this(new RawMaterialUnit(), string.Empty, findUnitOfMeasure: null)
    {
    }

    /// <param name="findUnitOfMeasure">Resolves a picked unit of measure, so a row names its unit as soon as it's picked, even in the add row.</param>
    public RawMaterialUnitRow(RawMaterialUnit unit, string baseUnitAbbreviation, Func<int, UnitOfMeasure?>? findUnitOfMeasure)
    {
        Unit = unit;
        _baseUnitAbbreviation = baseUnitAbbreviation;
        _findUnitOfMeasure = findUnitOfMeasure;
    }

    public RawMaterialUnit Unit { get; }

    /// <summary>The unit of measure picked in the grid; null until one is.</summary>
    public int? UnitOfMeasureId
    {
        get => Unit.UnitOfMeasureId > 0 ? Unit.UnitOfMeasureId : null;
        set
        {
            if (value == UnitOfMeasureId)
            {
                return;
            }

            Unit.UnitOfMeasureId = value ?? 0;
            Unit.UnitOfMeasure = value is { } id ? _findUnitOfMeasure?.Invoke(id) : null;
            this.RaisePropertyChanged();
            RaiseUnitOfMeasureChanged();
        }
    }

    /// <summary>Points the row at the screen's current copy of its unit of measure, so one renamed in Settings shows its new name.</summary>
    public void RefreshUnitOfMeasure()
    {
        if (UnitOfMeasureId is { } id && _findUnitOfMeasure?.Invoke(id) is { } refreshed && !ReferenceEquals(refreshed, Unit.UnitOfMeasure))
        {
            Unit.UnitOfMeasure = refreshed;
            RaiseUnitOfMeasureChanged();
        }
    }

    public string UnitName => Unit.UnitOfMeasure?.Name ?? "?";

    /// <summary>Empty until a unit is picked, so the add row stays blank.</summary>
    public string Abbreviation => Unit.UnitOfMeasure?.Abbreviation ?? string.Empty;

    /// <summary>How many base units one of this unit holds: 25,000 for a 25 kg bag of an ingredient.</summary>
    public decimal BaseUnitsPerUnit
    {
        get => Unit.BaseUnitsPerUnit;
        set
        {
            if (value == Unit.BaseUnitsPerUnit)
            {
                return;
            }

            Unit.BaseUnitsPerUnit = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(BaseEquivalentText));
        }
    }

    private string _baseUnitAbbreviation;
    /// <summary>The base unit's abbreviation, which a new packaging material can still change.</summary>
    public string BaseUnitAbbreviation
    {
        get => _baseUnitAbbreviation;
        set
        {
            this.RaiseAndSetIfChanged(ref _baseUnitAbbreviation, value);
            this.RaisePropertyChanged(nameof(BaseEquivalentText));
        }
    }

    /// <summary>E.g. "25,000 g" or "1,000 cm".</summary>
    public string BaseEquivalentText => $"{Unit.BaseUnitsPerUnit:#,0.####} {BaseUnitAbbreviation}";

    private void RaiseUnitOfMeasureChanged()
    {
        this.RaisePropertyChanged(nameof(UnitName));
        this.RaisePropertyChanged(nameof(Abbreviation));
    }
}

/// <summary>A row in the Price History grid: the entry, the unit it was priced in, and what that comes to per base unit.</summary>
public sealed class RawMaterialPriceRow(RawMaterialPriceHistory entry, string baseUnitName)
{
    public RawMaterialPriceHistory Entry { get; } = entry;

    public DateOnly Date => Entry.Date;

    public string UnitName => Entry.Unit?.UnitOfMeasure?.Name ?? baseUnitName;

    public decimal Price => Entry.Price;

    public decimal CostPerBaseUnit => UnitConversionRules.CostPerBaseUnit(Entry.Price, Entry.Unit?.BaseUnitsPerUnit);
}

/// <summary>What the Usage Example worked out: the calculated (true) cost, and that with the material's buffer on top.</summary>
public sealed record UsageExampleResult(
    string Title,
    decimal QuantityInBaseUnits,
    string BaseUnitAbbreviation,
    decimal CostPerBaseUnit,
    decimal TotalCost,
    decimal BufferPercent,
    decimal BufferedTotalCost);

/// <summary>
/// The Raw Materials screen: ingredients and packaging in one list. A material's type decides which
/// parts of its details apply — an ingredient is measured in grams and can be made from a BOM, a
/// packaging material has a base unit of its own and is always purchased. Its details, alternative
/// units, purchase price and BOM are all edited in memory, and Save writes them together.
/// </summary>
public class RawMaterialsViewModel : ViewModelBase
{
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterialCategory> _categoryValidator;
    private readonly IValidator<RawMaterial> _rawMaterialValidator;
    private readonly IValidator<RawMaterialUnit> _unitValidator;
    private readonly IValidator<RawMaterialPriceHistory> _priceHistoryValidator;
    private readonly SourceCache<RawMaterial, int> _rawMaterialCache = new(m => m.Id);
    private readonly ReadOnlyObservableCollection<RawMaterial> _rawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _visibleRawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _ingredients;
    private readonly ReadOnlyObservableCollection<RawMaterial> _linkTargets;
    private readonly ReadOnlyObservableCollection<RawMaterial> _bomComponentOptions;
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;
    private readonly SourceCache<RawMaterialCategory, int> _addNewCategoryOption = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categoryOptions;

    /// <summary>Every raw material, for the usage example's picker.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _rawMaterials;

    /// <summary>The materials the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<RawMaterial> VisibleRawMaterials => _visibleRawMaterials;

    /// <summary>
    /// The ingredients only: packaging never goes in a recipe. Resolves a BOM line's IngredientId and a saved
    /// LinkedToId back to their material; a picker that shouldn't offer the material being edited uses
    /// BomComponentOptions or LinkTargets instead.
    /// </summary>
    public ReadOnlyObservableCollection<RawMaterial> Ingredients => _ingredients;

    /// <summary>The ingredients a linked ingredient can be linked to: every one but the material being edited.</summary>
    public ReadOnlyObservableCollection<RawMaterial> LinkTargets => _linkTargets;

    /// <summary>The Bill of Materials component picker's list: every ingredient but the made one being edited — a made ingredient can't be its own component.</summary>
    public ReadOnlyObservableCollection<RawMaterial> BomComponentOptions => _bomComponentOptions;

    public ObservableCollection<RawMaterialPriceRow> PriceHistory { get; } = [];

    public ObservableCollection<IngredientLineRow> BomLineEdits { get; } = [];

    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    /// <summary>
    /// The Category picker's "&lt; Add New .. &gt;" row: a category-shaped stand-in for adding one, not a category
    /// itself. Its Id tells it apart from a saved category (a positive Id) and an unsaved one (0), and it's kept out
    /// of Categories, so the lookups and the refresh merge that work over real categories never see it.
    /// </summary>
    public const int AddNewCategoryId = -1;

    public static readonly RawMaterialCategory AddNewCategory = new() { Id = AddNewCategoryId, Name = "< Add New .. >" };

    /// <summary>What the Category picker lists: the categories, with the "&lt; Add New .. &gt;" row above them.</summary>
    public ReadOnlyObservableCollection<RawMaterialCategory> CategoryOptions => _categoryOptions;

    /// <summary>Every unit of measure, for the base unit picker and the Alternative Units grid's unit cell.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

    /// <summary>The form's alternative units, saved or not. Their units are the form's own copies, edited in place.</summary>
    public ObservableCollection<RawMaterialUnitRow> UnitRows { get; } = [];

    public ObservableCollection<RawMaterialUnitOption> PurchaseUnitOptions { get; } = [];

    public ObservableCollection<RawMaterialUnitOption> ExampleUnitOptions { get; } = [];

    public IReadOnlyList<RawMaterialType> Types { get; } = Enum.GetValues<RawMaterialType>();

    public IReadOnlyList<RawMaterialSource> Sources { get; } = Enum.GetValues<RawMaterialSource>();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    private RawMaterial? _selectedRawMaterial;
    public RawMaterial? SelectedRawMaterial
    {
        get => _selectedRawMaterial;
        set => this.RaiseAndSetIfChanged(ref _selectedRawMaterial, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    /// <summary>
    /// Flags EditingName as taken the moment it's left (SummaryHeaderCard's NameTextBox LostFocus), rather than
    /// waiting for Save to hit the same message from the unique-index violation (ViewModelBase.RunSafelyAsync).
    /// Case-insensitive and trimmed, matching the database's default collation the unique index relies on. Fixing
    /// the name and leaving the field again clears the message this raised, without touching an unrelated one.
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

    private RawMaterialType _editingType = RawMaterialType.Ingredient;
    public RawMaterialType EditingType
    {
        get => _editingType;
        set => this.RaiseAndSetIfChanged(ref _editingType, value);
    }

    private RawMaterialCategory? _editingCategory;
    public RawMaterialCategory? EditingCategory
    {
        get => _editingCategory;
        set
        {
            // The "< Add New .. >" row isn't a category to file the material under: AddCategoryCommand takes over,
            // and puts a category back in the picker once it's done.
            if (ReferenceEquals(value, AddNewCategory))
            {
                AddCategoryCommand.Execute().Subscribe();
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingCategory, value);
        }
    }

    private RawMaterialSource _editingSource = RawMaterialSource.Purchased;
    public RawMaterialSource EditingSource
    {
        get => _editingSource;
        set => this.RaiseAndSetIfChanged(ref _editingSource, value);
    }

    private decimal? _editingWeightLossPercent;
    public decimal? EditingWeightLossPercent
    {
        get => _editingWeightLossPercent;
        set => this.RaiseAndSetIfChanged(ref _editingWeightLossPercent, value);
    }

    /// <summary>Linked only: the ingredient this one is a part of.</summary>
    private RawMaterial? _editingLinkedTo;
    public RawMaterial? EditingLinkedTo
    {
        get => _editingLinkedTo;
        set => this.RaiseAndSetIfChanged(ref _editingLinkedTo, value);
    }

    /// <summary>Linked only: how much of the linked-to ingredient's weight this part is.</summary>
    private decimal? _editingWeightSharePercent;
    public decimal? EditingWeightSharePercent
    {
        get => _editingWeightSharePercent;
        set => this.RaiseAndSetIfChanged(ref _editingWeightSharePercent, value);
    }

    /// <summary>Linked only: how much of the linked-to ingredient's cost this part carries.</summary>
    private decimal? _editingCostSharePercent;
    public decimal? EditingCostSharePercent
    {
        get => _editingCostSharePercent;
        set => this.RaiseAndSetIfChanged(ref _editingCostSharePercent, value);
    }

    /// <summary>The material's cost buffer; empty saves as 0%, since the box shows 0.00 when cleared.</summary>
    private decimal? _editingCostBufferPercent = 0m;
    public decimal? EditingCostBufferPercent
    {
        get => _editingCostBufferPercent;
        set => this.RaiseAndSetIfChanged(ref _editingCostBufferPercent, value);
    }

    /// <summary>Packaging only; an ingredient's base unit is always the gram.</summary>
    private UnitOfMeasure? _editingBaseUnit;
    public UnitOfMeasure? EditingBaseUnit
    {
        get => _editingBaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingBaseUnit, value);
    }

    /// <summary>Only a new material's type and source can be picked: both are fixed once saved (RawMaterialRules).</summary>
    private bool _canChangeTypeAndSource = true;
    public bool CanChangeTypeAndSource
    {
        get => _canChangeTypeAndSource;
        private set => this.RaiseAndSetIfChanged(ref _canChangeTypeAndSource, value);
    }

    /// <summary>
    /// A packaging material's base unit can be picked while it's new, or has no saved alternative units
    /// or prices yet (RawMaterialRules.EnsureBaseUnitCanChange; the repository also checks packaging
    /// lines). Unsaved units don't count: they're saved against whichever base unit is picked then.
    /// </summary>
    private bool _canChangeBaseUnit = true;
    public bool CanChangeBaseUnit
    {
        get => _canChangeBaseUnit;
        private set => this.RaiseAndSetIfChanged(ref _canChangeBaseUnit, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isEditingIngredient;
    public bool IsEditingIngredient => _isEditingIngredient.Value;

    private readonly ObservableAsPropertyHelper<bool> _isEditingPackaging;
    public bool IsEditingPackaging => _isEditingPackaging.Value;

    private readonly ObservableAsPropertyHelper<bool> _isEditingMade;
    /// <summary>A made ingredient: it has a weight loss percent and a BOM instead of a purchase price.</summary>
    public bool IsEditingMade => _isEditingMade.Value;

    private readonly ObservableAsPropertyHelper<bool> _isEditingLinked;
    /// <summary>A linked ingredient: its cost is a share of another ingredient's, instead of a price or a BOM.</summary>
    public bool IsEditingLinked => _isEditingLinked.Value;

    private readonly ObservableAsPropertyHelper<string> _linkedCostPreviewText;
    /// <summary>What the link works out to as it's filled in: the linked-to ingredient's cost per gram, and this part's.</summary>
    public string LinkedCostPreviewText => _linkedCostPreviewText.Value;

    private readonly ObservableAsPropertyHelper<string> _linkedPartsText;
    /// <summary>The linked-to ingredient's other parts, and how much of its weight and cost all of them take with this one.</summary>
    public string LinkedPartsText => _linkedPartsText.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPurchaseSetup;
    /// <summary>A purchased material, saved or still being filled in, so a new one can be priced before its first Save.</summary>
    public bool ShowPurchaseSetup => _showPurchaseSetup.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPriceHistory;
    /// <summary>A saved purchased material: a new one has no price history yet.</summary>
    public bool ShowPriceHistory => _showPriceHistory.Value;

    /// <summary>The details tabs along the bottom: Cost Setup, Alternative Units, then Price History.</summary>
    public const int PriceHistoryTab = 2;

    /// <summary>The details tab showing. Moves to Cost Setup when Price History is about to hide (ShowPriceHistory).</summary>
    private int _selectedDetailsTab;
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => this.RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The summary header's big title: the material's name, or "New raw material".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string> _headerInitial;
    public string HeaderInitial => _headerInitial.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitName;
    /// <summary>The form's base unit name: "gram" for an ingredient, e.g. "centimeter" for ribbon.</summary>
    public string BaseUnitName => _baseUnitName.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitAbbreviation;
    public string BaseUnitAbbreviation => _baseUnitAbbreviation.Value;

    private readonly ObservableAsPropertyHelper<string> _currentCostText;
    /// <summary>E.g. "₱0.0372", or "—" for a material not saved yet.</summary>
    public string CurrentCostText => _currentCostText.Value;

    private readonly ObservableAsPropertyHelper<string> _bufferedCostText;
    /// <summary>The saved cost with the buffer on top, e.g. "₱0.0391", or "—" for a material not saved yet.</summary>
    public string BufferedCostText => _bufferedCostText.Value;

    /// <summary>The latest price's date, or "—" for an unpriced or made material.</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    // Alternative Units: typed straight into the grid; Remove acts on the row picked.
    private RawMaterialUnitRow? _selectedUnitRow;
    public RawMaterialUnitRow? SelectedUnitRow
    {
        get => _selectedUnitRow;
        set => this.RaiseAndSetIfChanged(ref _selectedUnitRow, value);
    }

    /// <summary>Whether the form can take another alternative unit (UnitConversionRules.MaxAlternativeUnits); the grid's add row shows while it can.</summary>
    private bool _canAddUnit = true;
    public bool CanAddUnit
    {
        get => _canAddUnit;
        private set => this.RaiseAndSetIfChanged(ref _canAddUnit, value);
    }

    /// <summary>
    /// The row the grid's add row edits (controls:GridAddNewRow.ItemFactory): one that names its unit as soon as it's
    /// picked and shows the current base unit, which a row the grid made itself couldn't.
    /// </summary>
    public Func<RawMaterialUnitRow> NewUnitRow => () => new RawMaterialUnitRow(new RawMaterialUnit(), BaseUnitAbbreviation, FindUnitOfMeasure);

    /// <summary>Set while the screen itself replaces or changes the unit rows, so only the user's grid edits count as edits.</summary>
    private bool _syncingUnitRows;

    // Purchase Cost Setup.
    private RawMaterialUnitOption? _editingPurchaseUnit;
    public RawMaterialUnitOption? EditingPurchaseUnit
    {
        get => _editingPurchaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseUnit, value);
    }

    /// <summary>The price per EditingPurchaseUnit. Saving a different price or unit adds a price history entry dated today.</summary>
    private decimal? _editingPrice;
    public decimal? EditingPrice
    {
        get => _editingPrice;
        set => this.RaiseAndSetIfChanged(ref _editingPrice, value);
    }

    /// <summary>
    /// How many base units one EditingPurchaseUnit holds: its multiplier. Typing it sets the multiplier
    /// of a unit that isn't saved yet; empty for one added here until it's typed.
    /// </summary>
    private decimal? _editingPurchaseQuantity;
    public decimal? EditingPurchaseQuantity
    {
        get => _editingPurchaseQuantity;
        set => this.RaiseAndSetIfChanged(ref _editingPurchaseQuantity, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isPurchaseQuantityReadOnly;
    /// <summary>
    /// Read-only for the base unit (always 1) and a saved alternative unit, whose multiplier changes
    /// in the Alternative Units grid, since a change reprices the material.
    /// </summary>
    public bool IsPurchaseQuantityReadOnly => _isPurchaseQuantityReadOnly.Value;

    /// <summary>
    /// The alternative unit added by picking a new unit in the Purchase Cost Setup. It stays only while
    /// it's the purchase unit, so picking another before Save takes it back out.
    /// </summary>
    private RawMaterialUnit? _purchaseAddedUnit;

    /// <summary>
    /// Set while the purchase unit choices are replaced or a unit is picked for the user, so neither the
    /// picker clearing itself nor the quantity box following the pick counts as the user's own change.
    /// </summary>
    private bool _syncingPurchaseSetup;

    /// <summary>
    /// EditingPrice with EditingCostBufferPercent on top: ₱930 at 5% is ₱976.50. Typing it works out the
    /// buffer percent instead, which is what's saved; a new price keeps the percent and moves this.
    /// </summary>
    private decimal? _editingBufferedPrice;
    public decimal? EditingBufferedPrice
    {
        get => _editingBufferedPrice;
        set => this.RaiseAndSetIfChanged(ref _editingBufferedPrice, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isBufferedPriceReadOnly;
    /// <summary>Read-only until there's a price, since the buffer percent is worked out from it.</summary>
    public bool IsBufferedPriceReadOnly => _isBufferedPriceReadOnly.Value;

    /// <summary>Set while the buffer percent or the buffered price follows the other, so that isn't taken as the user's own edit.</summary>
    private bool _syncingCostBuffer;

    private readonly ObservableAsPropertyHelper<string> _purchaseCostText;
    /// <summary>What EditingPrice comes to per base unit, recalculated as it's typed, e.g. "₱0.0372".</summary>
    public string PurchaseCostText => _purchaseCostText.Value;

    private readonly ObservableAsPropertyHelper<string> _purchaseBufferedCostText;
    /// <summary>PurchaseCostText with EditingCostBufferPercent on top, e.g. "Buffered (5%): ₱0.0391", or empty while unpriced.</summary>
    public string PurchaseBufferedCostText => _purchaseBufferedCostText.Value;

    // Usage Example.
    private RawMaterial? _exampleRawMaterial;
    public RawMaterial? ExampleRawMaterial
    {
        get => _exampleRawMaterial;
        set => this.RaiseAndSetIfChanged(ref _exampleRawMaterial, value);
    }

    private decimal _exampleQuantity = 1m;
    public decimal ExampleQuantity
    {
        get => _exampleQuantity;
        set => this.RaiseAndSetIfChanged(ref _exampleQuantity, value);
    }

    private RawMaterialUnitOption? _exampleUnit;
    public RawMaterialUnitOption? ExampleUnit
    {
        get => _exampleUnit;
        set => this.RaiseAndSetIfChanged(ref _exampleUnit, value);
    }

    private UsageExampleResult? _exampleResult;
    public UsageExampleResult? ExampleResult
    {
        get => _exampleResult;
        private set => this.RaiseAndSetIfChanged(ref _exampleResult, value);
    }

    // Bill of Materials (made ingredients): components and grams, typed straight into its table.
    private IngredientLineRow? _selectedBomLineEdit;
    public IngredientLineRow? SelectedBomLineEdit
    {
        get => _selectedBomLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedBomLineEdit, value);
    }

    /// <summary>The BOM lines as last loaded, so Save only sends the BOM when its lines were changed.</summary>
    private List<(int ComponentIngredientId, decimal Grams)> _savedBomLines = [];

    private RawMaterialPriceRow? _selectedPriceRow;
    public RawMaterialPriceRow? SelectedPriceRow
    {
        get => _selectedPriceRow;
        set => this.RaiseAndSetIfChanged(ref _selectedPriceRow, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBomLineCommand { get; }

    /// <summary>
    /// Input: the label to put over the box. Output: the name typed for the new category, or null if it was
    /// cancelled. The View registers a handler that shows a prompt dialog (Views/TextPromptWindow).
    /// </summary>
    public Interaction<string, string?> PromptForNewCategory { get; } = new();

    public RawMaterialsViewModel(
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IIngredientBomLineRepository bomLineRepository,
        IRawMaterialCategoryRepository categoryRepository,
        IValidator<RawMaterialCategory> categoryValidator,
        IValidator<RawMaterial> rawMaterialValidator,
        IValidator<RawMaterialUnit> unitValidator,
        IValidator<RawMaterialPriceHistory> priceHistoryValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        ILogger logger)
        : base(logger)
    {
        _rawMaterialRepository = rawMaterialRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _bomLineRepository = bomLineRepository;
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;
        _rawMaterialValidator = rawMaterialValidator;
        _unitValidator = unitValidator;
        _priceHistoryValidator = priceHistoryValidator;

        var byName = SortExpressionComparer<RawMaterial>.Ascending(m => m.Name);

        _rawMaterialCache.Connect()
            .SortAndBind(out _rawMaterials, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<RawMaterial, bool>)(m => text.Length == 0 || m.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        _rawMaterialCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleRawMaterials, byName)
            .Subscribe();

        _rawMaterialCache.Connect()
            .Filter(m => m.Type == RawMaterialType.Ingredient)
            .SortAndBind(out _ingredients, byName)
            .Subscribe();

        // Shared by every picker that must leave out the ingredient being edited: Linked's target and the BOM's component.
        var notTheSelection = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(selected => selected?.Id ?? 0)
            .DistinctUntilChanged()
            .Select(selectedId => (Func<RawMaterial, bool>)(m => m.Type == RawMaterialType.Ingredient && m.Id != selectedId));

        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _linkTargets, byName)
            .Subscribe();

        _rawMaterialCache.Connect()
            .Filter(notTheSelection)
            .SortAndBind(out _bomComponentOptions, byName)
            .Subscribe();

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
            .Subscribe();

        // The "< Add New .. >" row comes from a cache of its own rather than _categoryCache, which LoadCommand's
        // MergeFrom diffs against the categories it loaded — the row isn't one of them, so it would be dropped.
        _addNewCategoryOption.AddOrUpdate(AddNewCategory);

        _categoryCache.Connect()
            .Or(_addNewCategoryOption.Connect())
            .SortAndBind(out _categoryOptions, SortExpressionComparer<RawMaterialCategory>
                .Ascending(c => c.Id == AddNewCategoryId ? 0 : 1)
                .ThenByAscending(c => c.Name))
            .Subscribe();

        _isEditingIngredient = this.WhenAnyValue(x => x.EditingType)
            .Select(type => type == RawMaterialType.Ingredient)
            .ToProperty(this, x => x.IsEditingIngredient);

        _isEditingPackaging = this.WhenAnyValue(x => x.EditingType)
            .Select(type => type == RawMaterialType.Packaging)
            .ToProperty(this, x => x.IsEditingPackaging);

        _isEditingMade = this.WhenAnyValue(x => x.EditingType, x => x.EditingSource,
                (type, source) => type == RawMaterialType.Ingredient && source == RawMaterialSource.Made)
            .ToProperty(this, x => x.IsEditingMade);

        _isEditingLinked = this.WhenAnyValue(x => x.EditingType, x => x.EditingSource,
                (type, source) => type == RawMaterialType.Ingredient && source == RawMaterialSource.Linked)
            .ToProperty(this, x => x.IsEditingLinked);

        _linkedCostPreviewText = this.WhenAnyValue(x => x.EditingLinkedTo, x => x.EditingWeightSharePercent, x => x.EditingCostSharePercent,
                LinkedCostPreview)
            .ToProperty(this, x => x.LinkedCostPreviewText, string.Empty);

        _linkedPartsText = this.WhenAnyValue(x => x.EditingLinkedTo, x => x.EditingWeightSharePercent, x => x.EditingCostSharePercent,
                LinkedPartsSummary)
            .ToProperty(this, x => x.LinkedPartsText, string.Empty);

        // The cards follow the form, not the selection, so a new material can be filled in completely
        // before its one Save.
        _showPurchaseSetup = this.WhenAnyValue(x => x.EditingType, x => x.EditingSource,
                (type, source) => type == RawMaterialType.Packaging || source == RawMaterialSource.Purchased)
            .ToProperty(this, x => x.ShowPurchaseSetup);

        // A hidden tab can't stay the one showing: picking a made or new material while on Price
        // History goes back to Cost Setup. Subscribed before ShowPriceHistory, so the tab strip moves
        // before the tab hides; a TabControlExt whose selected tab hides picks a neighbour itself.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(material => material is not { Source: RawMaterialSource.Purchased } && SelectedDetailsTab == PriceHistoryTab)
            .Subscribe(_ => SelectedDetailsTab = 0);

        _showPriceHistory = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is { Source: RawMaterialSource.Purchased })
            .ToProperty(this, x => x.ShowPriceHistory);

        _headerTitle = this.WhenAnyValue(x => x.SelectedRawMaterial, x => x.EditingName, (material, name) =>
                material is null && string.IsNullOrWhiteSpace(name) ? "New raw material" : name)
            .ToProperty(this, x => x.HeaderTitle, "New raw material");

        _headerInitial = this.WhenAnyValue(x => x.HeaderTitle)
            .Select(title => string.IsNullOrWhiteSpace(title) ? "?" : title.Trim()[..1].ToUpperInvariant())
            .ToProperty(this, x => x.HeaderInitial, "N");

        _baseUnitName = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.NameOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitName, "gram");

        _baseUnitAbbreviation = this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit,
                (type, baseUnit) => BaseUnitText.AbbreviationOf(type, baseUnit))
            .ToProperty(this, x => x.BaseUnitAbbreviation, "g");

        _currentCostText = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is null ? "—" : $"₱{material.CostPerBaseUnit:N4}")
            .ToProperty(this, x => x.CurrentCostText, "—");

        _bufferedCostText = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is null ? "—" : $"₱{material.BufferedCostPerBaseUnit:N4}")
            .ToProperty(this, x => x.BufferedCostText, "—");

        _purchaseCostText = this.WhenAnyValue(x => x.EditingPrice, x => x.EditingPurchaseQuantity, (price, quantity) =>
                price is > 0 && quantity is > 0
                    ? $"₱{UnitConversionRules.CostPerBaseUnit(price.Value, quantity):N4}"
                    : "—")
            .ToProperty(this, x => x.PurchaseCostText, "—");

        _purchaseBufferedCostText = this.WhenAnyValue(x => x.EditingPrice, x => x.EditingPurchaseQuantity, x => x.EditingCostBufferPercent,
                (price, quantity, buffer) =>
                    price is > 0 && quantity is > 0
                        ? $"Buffered ({buffer ?? 0m:0.##}%): ₱{RawMaterialRules.BufferedCost(UnitConversionRules.CostPerBaseUnit(price.Value, quantity), buffer ?? 0m):N4}"
                        : string.Empty)
            .ToProperty(this, x => x.PurchaseBufferedCostText, string.Empty);

        _isPurchaseQuantityReadOnly = this.WhenAnyValue(x => x.EditingPurchaseUnit)
            .Select(option => option?.Unit is not { Id: 0 })
            .ToProperty(this, x => x.IsPurchaseQuantityReadOnly, true);

        _isBufferedPriceReadOnly = this.WhenAnyValue(x => x.EditingPrice)
            .Select(price => price is not > 0)
            .ToProperty(this, x => x.IsBufferedPriceReadOnly, true);

        // The buffer percent and the buffered price follow each other: typing either works out the
        // other from the price. The percent is what's saved, so a new price keeps it and moves the
        // buffered price. Loading a material sets the percent and price, so this runs then too.
        this.WhenAnyValue(x => x.EditingPrice, x => x.EditingCostBufferPercent)
            .Where(_ => !_syncingCostBuffer)
            .Subscribe(values => SyncCostBuffer(() => EditingBufferedPrice = values.Item1 is { } price && price > 0
                ? Math.Round(RawMaterialRules.BufferedCost(price, values.Item2 ?? 0m), 2)
                : null));

        this.WhenAnyValue(x => x.EditingBufferedPrice)
            .Where(bufferedPrice => !_syncingCostBuffer && bufferedPrice is not null && EditingPrice is > 0)
            .Subscribe(bufferedPrice => SyncCostBuffer(() =>
                EditingCostBufferPercent = Math.Round(RawMaterialRules.CostBufferPercentOf(EditingPrice!.Value, bufferedPrice!.Value), 4)));

        // Picking a unit the material doesn't have yet adds it as an alternative unit; picking any other
        // takes back one added that way, so browsing the picker leaves nothing behind.
        this.WhenAnyValue(x => x.EditingPurchaseUnit)
            .Where(_ => !IsRefreshing && !_syncingPurchaseSetup)
            .Subscribe(OnPurchaseUnitPicked);

        // The quantity typed for a unit that isn't saved yet is its multiplier, which the Alternative
        // Units grid and the overview show as it's typed.
        this.WhenAnyValue(x => x.EditingPurchaseQuantity)
            .Where(_ => !IsRefreshing && !_syncingPurchaseSetup)
            .Subscribe(quantity =>
            {
                if (EditingPurchaseUnit?.Unit is { Id: 0 } unit && UnitRows.FirstOrDefault(r => ReferenceEquals(r.Unit, unit)) is { } row)
                {
                    SyncUnitRows(() => row.BaseUnitsPerUnit = quantity ?? 0m);
                }
            });

        // A packaging material is always purchased, and an ingredient's base unit is the gram, so
        // switching a new material's type clears whatever the other type had filled in.
        this.WhenAnyValue(x => x.EditingType)
            .Where(_ => !IsRefreshing)
            .Subscribe(type =>
            {
                if (type == RawMaterialType.Packaging)
                {
                    EditingSource = RawMaterialSource.Purchased;
                    EditingWeightLossPercent = null;
                    EditingLinkedTo = null;
                    EditingWeightSharePercent = null;
                    EditingCostSharePercent = null;
                }
                else
                {
                    EditingBaseUnit = null;
                }
            });

        // A made ingredient starts with no weight loss: 0%, which is what the empty box shows anyway.
        this.WhenAnyValue(x => x.EditingSource)
            .Where(source => source == RawMaterialSource.Made && !IsRefreshing)
            .Subscribe(_ => EditingWeightLossPercent ??= 0m);

        // The alternative units and the purchase unit choices name the base unit, which a new
        // material can still change.
        this.WhenAnyValue(x => x.EditingType, x => x.EditingBaseUnit)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ => RebuildUnitViews());

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which
        // only re-points a selection at its refreshed copy; the form and details must stay as-is.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(material => material is not null && !IsRefreshing)
            .Subscribe(material => LoadIntoForm(material!));

        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(ResetDetails);

        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(_ => !IsRefreshing)
            .Select(material => Observable.FromAsync(() => LoadPriceHistoryAsync(material)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(_ => !IsRefreshing)
            .Select(material => Observable.FromAsync(() => LoadBomLinesAsync(material)))
            .Switch()
            .Subscribe();

        // A unit typed into the Alternative Units grid, or its unit or multiplier changed there, is offered as a
        // purchase unit straight away, and the add row goes once there are two.
        UnitRows.ToObservableChangeSet()
            .AutoRefresh(r => r.UnitOfMeasureId)
            .AutoRefresh(r => r.BaseUnitsPerUnit)
            .Where(_ => !_syncingUnitRows && !IsRefreshing)
            .Subscribe(_ => OnUnitRowsEdited());

        // A component picked in the BOM table is pointed at the screen's copy of it, for its name.
        BomLineEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .Subscribe(_ => IngredientLineRow.Resolve(BomLineEdits, Ingredients.ToDictionary(m => m.Id)));

        this.WhenAnyValue(x => x.ExampleRawMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material => ReplaceExampleUnitOptions(material, kept: null));

        // A worked-out example belongs to the inputs it was worked out from.
        this.WhenAnyValue(x => x.ExampleRawMaterial, x => x.ExampleQuantity, x => x.ExampleUnit)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ => ExampleResult = null);

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, picker choices and unsaved edits
        // while picking up what changed elsewhere, such as a unit renamed in Settings.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await _rawMaterialRepository.GetAllAsync(CancellationToken.None);

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedRawMaterial?.Id;
            var categoryId = EditingCategory?.Id;
            var linkedToId = EditingLinkedTo?.Id;
            var baseUnitId = EditingBaseUnit?.Id;
            var exampleMaterialId = ExampleRawMaterial?.Id;
            var exampleUnit = ExampleUnit;

            ApplyRefresh(() =>
            {
                _categoryCache.MergeFrom(categories);
                UnitsOfMeasure.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
                _rawMaterialCache.MergeFrom(rawMaterials);

                EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
                EditingLinkedTo = Ingredients.FirstOrDefault(m => m.Id == linkedToId);
                var previousBaseUnit = EditingBaseUnit;
                EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId);

                IngredientLineRow.Resolve(BomLineEdits, Ingredients.ToDictionary(m => m.Id));

                SelectedRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == selectedId);

                // The form's alternative units may not be saved yet, so they stay rather than being
                // reloaded from the refreshed material. Each points at its refreshed unit of measure,
                // so one renamed in Settings shows its new name.
                foreach (var row in UnitRows)
                {
                    row.RefreshUnitOfMeasure();
                }

                if (!ReferenceEquals(previousBaseUnit, EditingBaseUnit))
                {
                    UpdateUnitRowsBaseUnit();
                }

                // Every time, so a unit of measure created in Settings shows as a new purchase unit.
                RebuildPurchaseUnitOptions();

                var previousExample = ExampleRawMaterial;
                ExampleRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == exampleMaterialId);
                if (!ReferenceEquals(previousExample, ExampleRawMaterial))
                {
                    ReplaceExampleUnitOptions(ExampleRawMaterial, exampleUnit);
                    ExampleResult = null;
                }
            });
        });

        NewCommand = ReactiveCommand.Create(ClearForm);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var type = EditingType;
            var source = type == RawMaterialType.Packaging ? RawMaterialSource.Purchased : EditingSource;
            var material = new RawMaterial
            {
                Id = _editingId,
                Name = EditingName,
                Type = type,
                CategoryId = EditingCategory?.Id ?? 0,
                Source = source,
                // A cleared box shows 0.00, so it saves as 0%.
                WeightLossPercent = source == RawMaterialSource.Made ? EditingWeightLossPercent ?? 0m : null,
                LinkedToId = source == RawMaterialSource.Linked ? EditingLinkedTo?.Id : null,
                WeightSharePercent = source == RawMaterialSource.Linked ? EditingWeightSharePercent : null,
                CostSharePercent = source == RawMaterialSource.Linked ? EditingCostSharePercent : null,
                BaseUnitId = type == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null,
                // A cleared box shows 0.00 too. A linked ingredient inherits the buffer of the one it's linked to
                // (the repository sets it from the saved one either way).
                CostBufferPercent = source == RawMaterialSource.Linked
                    ? EditingLinkedTo?.CostBufferPercent ?? 0m
                    : EditingCostBufferPercent ?? 0m,
            };

            var validation = _rawMaterialValidator.Validate(material);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            // A new material's purchase unit starts empty, and a price means nothing without the unit it's for.
            if (source == RawMaterialSource.Purchased && EditingPurchaseUnit is null && EditingPrice is not (null or 0m))
            {
                ErrorMessage = "Pick the purchase unit the price is for.";
                return;
            }

            // The units are typed straight into the grid, so a half-filled or repeated one is caught here.
            EnsureValidUnitRows();

            // The picker already leaves the material being edited out of BomComponentOptions, so this can't be
            // picked through the UI. But a self-reference already in the database (only reachable by editing it
            // directly, never through this app) would otherwise slip past: ChangedBomLines only re-validates a
            // BOM that changed, so an untouched one skips IngredientBomRules.EnsureNoCycle entirely on a Save
            // that touches something else. This runs unconditionally, whether or not the BOM itself changed.
            if (source == RawMaterialSource.Made && BomLineEdits.Any(l => l.IngredientId == _editingId))
            {
                ErrorMessage = "An ingredient can't be a component of itself.";
                return;
            }

            (DateOnly Date, int? UnitOfMeasureId, decimal Price)? newPrice = null;
            if (source == RawMaterialSource.Purchased && HasNewPrice())
            {
                var entry = new RawMaterialPriceHistory { Price = EditingPrice ?? 0m };
                var priceValidation = _priceHistoryValidator.Validate(entry);
                if (!priceValidation.IsValid)
                {
                    ErrorMessage = string.Join(" ", priceValidation.Errors.Select(e => e.ErrorMessage));
                    return;
                }

                newPrice = (DateOnly.FromDateTime(DateTime.Today), EditingPurchaseUnit!.Unit?.UnitOfMeasureId, entry.Price);
            }

            var bomLines = source == RawMaterialSource.Made ? ChangedBomLines() : null;

            var id = await _rawMaterialRepository.SaveAsync(material, [.. UnitRows.Select(r => r.Unit)], newPrice, bomLines, CancellationToken.None);

            // The list picks up any cost that cascaded to other materials; then the form reloads what
            // was saved, so new units get their ids and the price history and BOM show the new rows.
            await LoadCommand.Execute();
            await ReloadSavedRawMaterialAsync(id);
            SuccessMessage = $"{EditingName} was saved.";
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedRawMaterial!.Name}'? This can't be undone.", async () =>
            {
                await _rawMaterialRepository.DeleteAsync(SelectedRawMaterial!.Id, CancellationToken.None);
                ClearForm();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedRawMaterial).Select(material => material is not null));

        // The Category picker's "< Add New .. >" row: names a category, saves it, and picks it for the material
        // being edited, so filing one under a new category doesn't mean a trip to Settings and back.
        AddCategoryCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var name = await PromptForNewCategory.Handle("Name for the new category:");

            if (!string.IsNullOrWhiteSpace(name))
            {
                await RunSafelyAsync(async () =>
                {
                    var category = new RawMaterialCategory { Name = name.Trim() };

                    var validation = _categoryValidator.Validate(category);
                    if (!validation.IsValid)
                    {
                        ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                        return;
                    }

                    // AddAsync fills in the saved Id, which the reloaded copy to pick is found by.
                    await _categoryRepository.AddAsync(category, CancellationToken.None);
                    await LoadCommand.Execute();
                    EditingCategory = Categories.FirstOrDefault(c => c.Id == category.Id);
                    SuccessMessage = $"{category.Name} was added.";
                });
            }

            // The picker is still showing the row that was picked, so the category goes back into it: the new one,
            // or the one it had when the prompt was cancelled or the name refused. Pushed back here rather than
            // from the setter, since a value raised during the pick's own binding update is ignored.
            this.RaisePropertyChanged(nameof(EditingCategory));
        });

        var hasSelectedUnitRow = this.WhenAnyValue(x => x.SelectedUnitRow).Select(row => row is not null);

        // Nothing to confirm: the unit only leaves the list, and is deleted when the material is saved.
        DeleteUnitCommand = ReactiveCommand.Create(
            () =>
            {
                var removed = SelectedUnitRow!.Unit;
                if (ReferenceEquals(removed, _purchaseAddedUnit))
                {
                    _purchaseAddedUnit = null;
                }

                ReplaceUnitRows([.. UnitRows.Select(r => r.Unit).Where(u => !ReferenceEquals(u, removed))], selectedUnit: null);
                RebuildPurchaseUnitOptions();
            },
            hasSelectedUnitRow);

        CalculateExampleCommand = ReactiveCommand.Create(
            () =>
            {
                var material = ExampleRawMaterial!;
                var unit = ExampleUnit!;
                var baseQuantity = UnitConversionRules.ToBaseQuantity(ExampleQuantity, unit.Unit?.BaseUnitsPerUnit);
                ExampleResult = new UsageExampleResult(
                    $"Cost for {ExampleQuantity:#,0.####} {unit.Abbreviation} of {material.Name}",
                    baseQuantity,
                    BaseUnitText.AbbreviationOf(material),
                    material.CostPerBaseUnit,
                    baseQuantity * material.CostPerBaseUnit,
                    material.CostBufferPercent,
                    baseQuantity * material.BufferedCostPerBaseUnit);
            },
            this.WhenAnyValue(x => x.ExampleRawMaterial, x => x.ExampleUnit, x => x.ExampleQuantity,
                (material, unit, quantity) => material is not null && unit is not null && quantity > 0));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price entry? This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceRow!.Entry.Id, CancellationToken.None);

                // A merge picks up the material's new cost and keeps the form's unsaved edits, which
                // selecting the refreshed material would reload away. Only the history itself reloads.
                await LoadCommand.Execute();
                await LoadPriceHistoryAsync(SelectedRawMaterial);
            }),
            this.WhenAnyValue(x => x.SelectedPriceRow).Select(row => row is not null));

        RemoveBomLineCommand = ReactiveCommand.Create(
            () => { BomLineEdits.Remove(SelectedBomLineEdit!); },
            this.WhenAnyValue(x => x.SelectedBomLineEdit).Select(line => line is not null));
    }

    /// <summary>
    /// "Egg costs ₱0.0900/g before its 10% buffer. At 70% of its weight for 30% of its cost, this costs ₱0.0386/g, buffered ₱0.0424/g."
    /// Worked out with the same rules the cascade saves with, from the linked-to ingredient's stored true cost
    /// and buffer: the cost share is of the unbuffered cost, and the linked-to ingredient's buffer percent,
    /// which the part inherits, goes on top.
    /// </summary>
    private static string LinkedCostPreview(RawMaterial? linkedTo, decimal? weightShare, decimal? costShare)
    {
        if (linkedTo is null)
        {
            return "Pick the ingredient this one is a part of.";
        }

        var linkedToText = $"{linkedTo.Name} costs ₱{linkedTo.CostPerBaseUnit:N4}/g before its {linkedTo.CostBufferPercent:0.##}% buffer.";
        if (weightShare is not (> 0 and <= 100) || costShare is not (> 0 and <= 100))
        {
            return $"{linkedToText} Enter this part's share of its weight and of its cost.";
        }

        var cost = RawMaterialRules.LinkedCost(linkedTo.CostPerBaseUnit, weightShare.Value, costShare.Value);
        var buffered = RawMaterialRules.BufferedCost(cost, linkedTo.CostBufferPercent);
        return $"{linkedToText} At {weightShare:0.##}% of its weight for {costShare:0.##}% of its cost, this costs ₱{cost:N4}/g, buffered ₱{buffered:N4}/g.";
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

    /// <summary>Sets the buffer percent or the buffered price from the other without that setting the first one back.</summary>
    private void SyncCostBuffer(Action set)
    {
        _syncingCostBuffer = true;
        try
        {
            set();
        }
        finally
        {
            _syncingCostBuffer = false;
        }
    }

    /// <summary>
    /// An empty form, for New and after Delete. It starts as a purchased ingredient, the usual kind, but
    /// the category, purchase unit and the usage example's material start empty, so none is saved or
    /// costed by accident. The numbers keep a value: the price and cost buffer are 0, and the usage
    /// example's quantity stays.
    /// </summary>
    private void ClearForm()
    {
        _editingId = 0;
        SelectedRawMaterial = null;
        CanChangeTypeAndSource = true;
        EditingName = string.Empty;
        EditingType = RawMaterialType.Ingredient;
        EditingCategory = null;
        EditingSource = RawMaterialSource.Purchased;
        EditingWeightLossPercent = null;
        EditingLinkedTo = null;
        EditingWeightSharePercent = null;
        EditingCostSharePercent = null;
        EditingCostBufferPercent = 0m;
        EditingBaseUnit = null;
        ExampleRawMaterial = null;
        ClearDetails();
    }

    private void LoadIntoForm(RawMaterial material)
    {
        _editingId = material.Id;
        CanChangeTypeAndSource = false;
        EditingName = material.Name;
        EditingType = material.Type;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == material.CategoryId);
        EditingSource = material.Source;
        EditingWeightLossPercent = material.WeightLossPercent;
        EditingLinkedTo = Ingredients.FirstOrDefault(m => m.Id == material.LinkedToId);
        EditingWeightSharePercent = material.WeightSharePercent;
        EditingCostSharePercent = material.CostSharePercent;
        EditingCostBufferPercent = material.CostBufferPercent;
        EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId);
    }

    /// <summary>What follows a new selection: its alternative units, and nothing picked or open.</summary>
    private void ResetDetails(RawMaterial? material)
    {
        SelectedPriceRow = null;
        SelectedBomLineEdit = null;
        LoadUnitRows(material);
        if (material is not null)
        {
            ExampleRawMaterial = material;
        }
    }

    /// <summary>
    /// A new material's details: no alternative units, price history or BOM lines. Selecting nothing
    /// clears them only when something was selected, and after an unsaved new material nothing was.
    /// </summary>
    private void ClearDetails()
    {
        ResetDetails(null);
        PriceHistory.Clear();
        LastUpdatedText = "—";
        BomLineEdits.Clear();
        _savedBomLines = [];
        UpdateCanChangeBaseUnit();
        ResetPurchaseSetup();
    }

    /// <summary>The form's alternative units, from <paramref name="material"/>'s saved ones (none for a new material).</summary>
    private void LoadUnitRows(RawMaterial? material)
    {
        // Copies, since the form edits its units in place and the cached material must stay as saved.
        // Each points at the Units of Measure list's own object, which refreshes keep current.
        var units = (material?.Units ?? []).Select(u => new RawMaterialUnit
        {
            Id = u.Id,
            RawMaterialId = u.RawMaterialId,
            UnitOfMeasureId = u.UnitOfMeasureId,
            UnitOfMeasure = UnitsOfMeasure.FirstOrDefault(m => m.Id == u.UnitOfMeasureId) ?? u.UnitOfMeasure,
            BaseUnitsPerUnit = u.BaseUnitsPerUnit,
            Description = u.Description,
        });

        _purchaseAddedUnit = null;
        ReplaceUnitRows([.. units], selectedUnit: null);
        RebuildPurchaseUnitOptions();
        UpdateCanChangeBaseUnit();
    }

    /// <summary>Shows the base unit on the form's alternative units, and rebuilds the purchase unit choices, keeping what's picked.</summary>
    private void RebuildUnitViews()
    {
        UpdateUnitRowsBaseUnit();
        RebuildPurchaseUnitOptions();
    }

    /// <summary>Tells each row the base unit, which its multiplier is in, without replacing the rows the grid is showing.</summary>
    private void UpdateUnitRowsBaseUnit()
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        foreach (var row in UnitRows)
        {
            row.BaseUnitAbbreviation = baseUnitAbbreviation;
        }
    }

    private void ReplaceUnitRows(IReadOnlyList<RawMaterialUnit> units, RawMaterialUnit? selectedUnit)
    {
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(EditingType, EditingBaseUnit);
        SyncUnitRows(() =>
        {
            SelectedUnitRow = null;
            UnitRows.Clear();
            UnitRows.AddRange(units.Select(u => new RawMaterialUnitRow(u, baseUnitAbbreviation, FindUnitOfMeasure)));
            SelectedUnitRow = UnitRows.FirstOrDefault(r => ReferenceEquals(r.Unit, selectedUnit));
        });
        CanAddUnit = UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;
    }

    /// <summary>Changes the unit rows for the user without that counting as their edit (OnUnitRowsEdited).</summary>
    private void SyncUnitRows(Action change)
    {
        _syncingUnitRows = true;
        try
        {
            change();
        }
        finally
        {
            _syncingUnitRows = false;
        }
    }

    private UnitOfMeasure? FindUnitOfMeasure(int id) => UnitsOfMeasure.FirstOrDefault(u => u.Id == id);

    /// <summary>
    /// What an edit in the Alternative Units grid does: a row added there, or a row's unit or multiplier changed, shows in
    /// the purchase unit choices straight away, and the add row goes once there are two units.
    /// </summary>
    private void OnUnitRowsEdited()
    {
        CanAddUnit = UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;
        RebuildPurchaseUnitOptions();
    }

    /// <summary>
    /// The checks the Alternative Units form's Apply used to make, now that units are typed straight into the grid: every row has a
    /// unit and a positive multiplier, none is the base unit or repeats another's unit, and there are at most two. Throws
    /// InvalidOperationException for the first problem, which RunSafelyAsync shows.
    /// </summary>
    private void EnsureValidUnitRows()
    {
        var units = UnitRows.Select(r => r.Unit).ToList();
        foreach (var unit in units)
        {
            if (unit.UnitOfMeasureId <= 0)
            {
                throw new InvalidOperationException("Pick a unit for every alternative unit, or remove the empty row.");
            }

            if (unit.BaseUnitsPerUnit <= 0)
            {
                // A unit added by picking it as the purchase unit gets its multiplier from Quantity per Purchase Unit.
                var where = ReferenceEquals(unit, _purchaseAddedUnit) ? "Quantity per Purchase Unit" : "Alternative Units";
                throw new InvalidOperationException($"Enter how many {BaseUnitAbbreviation} are in one {unit.UnitOfMeasure?.Name ?? "unit"} ({where}).");
            }

            var validation = _unitValidator.Validate(unit);
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }

            RawMaterialRules.EnsureNotTheBaseUnit(EditingType == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null, unit.UnitOfMeasureId);
            RawMaterialRules.EnsureUnitNotAlreadyAdded(units.Any(u => !ReferenceEquals(u, unit) && u.UnitOfMeasureId == unit.UnitOfMeasureId));
        }

        if (units.Count > UnitConversionRules.MaxAlternativeUnits)
        {
            UnitConversionRules.EnsureCanAddAlternativeUnit(units.Count - 1);
        }
    }

    /// <summary>
    /// The Purchase Cost Setup's units: the base unit, then the form's alternative units, saved or not,
    /// then, while there's room for another, the units of measure it doesn't have yet (marked new).
    /// Keeps the unit picked while it's still there; otherwise picks as ResetPurchaseSetup does.
    /// </summary>
    private void RebuildPurchaseUnitOptions()
    {
        // Read first: the picker can clear its own selection when its items go.
        var picked = EditingPurchaseUnit;

        var form = new RawMaterial { Type = EditingType, BaseUnit = EditingBaseUnit, Units = [.. UnitRows.Select(r => r.Unit)] };
        _syncingPurchaseSetup = true;
        try
        {
            PurchaseUnitOptions.Clear();
            PurchaseUnitOptions.AddRange(RawMaterialUnitOption.For(form));
            PurchaseUnitOptions.AddRange(NewPurchaseUnitOptions(form));
        }
        finally
        {
            _syncingPurchaseSetup = false;
        }

        SetPurchaseUnit((picked is { IsNew: false } ? PurchaseUnitOptions.FirstOrDefault(o => !o.IsNew && ReferenceEquals(o.Unit, picked.Unit)) : null)
            ?? DefaultPurchaseUnit());
    }

    /// <summary>
    /// The units of measure the form doesn't have yet, which picking as the purchase unit adds. Only
    /// while there's room for another alternative unit; the one already added here doesn't count, since
    /// picking a new unit replaces it. The base unit is never offered: a packaging material's own, or a
    /// unit of measure named like an ingredient's gram, which has no unit of measure row of its own.
    /// </summary>
    private List<RawMaterialUnitOption> NewPurchaseUnitOptions(RawMaterial form)
    {
        if (UnitRows.Count(r => !ReferenceEquals(r.Unit, _purchaseAddedUnit)) >= UnitConversionRules.MaxAlternativeUnits)
        {
            return [];
        }

        var baseUnitId = EditingType == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null;
        return
        [
            .. UnitsOfMeasure
                .Where(u => u.Id != baseUnitId && UnitRows.All(r => r.Unit.UnitOfMeasureId != u.Id) && !IsNamedLikeTheBaseUnit(u))
                .Select(u => new RawMaterialUnitOption(new RawMaterialUnit { UnitOfMeasureId = u.Id, UnitOfMeasure = u }, form, isNew: true)),
        ];
    }

    private bool IsNamedLikeTheBaseUnit(UnitOfMeasure unit) =>
        string.Equals(unit.Name, BaseUnitName, StringComparison.CurrentCultureIgnoreCase)
        || string.Equals(unit.Abbreviation, BaseUnitAbbreviation, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// What picking a purchase unit does: a new one becomes an alternative unit, and any other takes back
    /// one added that way. Neither replaces the picker's items: a ComboBoxAdv whose items are replaced
    /// while it's still handling the pick falls back to its first item. So the picked option becomes
    /// the added unit's own, and only its IsNew flag changes.
    /// </summary>
    private void OnPurchaseUnitPicked(RawMaterialUnitOption? picked)
    {
        // The picker clears itself while its items are replaced.
        if (picked is null)
        {
            return;
        }

        if (!ReferenceEquals(picked.Unit, _purchaseAddedUnit))
        {
            DropPurchaseAddedUnit();
        }

        if (picked.IsNew)
        {
            picked.IsNew = false;
            _purchaseAddedUnit = picked.Unit;
            ReplaceUnitRows([.. UnitRows.Select(r => r.Unit), picked.Unit!], SelectedUnitRow?.Unit);
        }

        SetPurchaseUnit(picked);
    }

    /// <summary>
    /// Takes the unit added through the Purchase Cost Setup back out of the rows, and makes its option
    /// new again, in case it's picked again.
    /// </summary>
    private void DropPurchaseAddedUnit()
    {
        var added = _purchaseAddedUnit;
        if (added is null)
        {
            return;
        }

        _purchaseAddedUnit = null;
        ReplaceUnitRows([.. UnitRows.Select(r => r.Unit).Where(u => !ReferenceEquals(u, added))], SelectedUnitRow?.Unit);
        if (PurchaseUnitOptions.FirstOrDefault(o => ReferenceEquals(o.Unit, added)) is { } option)
        {
            option.IsNew = true;
        }
    }

    /// <summary>Picks a purchase unit for the user and shows its quantity: its multiplier, or empty for a unit added here that has none yet.</summary>
    private void SetPurchaseUnit(RawMaterialUnitOption? option)
    {
        _syncingPurchaseSetup = true;
        try
        {
            EditingPurchaseUnit = option;
            EditingPurchaseQuantity = option is { BaseUnitsPerUnit: > 0 } ? option.BaseUnitsPerUnit : null;
        }
        finally
        {
            _syncingPurchaseSetup = false;
        }
    }

    private void UpdateCanChangeBaseUnit() =>
        CanChangeBaseUnit = SelectedRawMaterial is null || (SelectedRawMaterial.Units.Count == 0 && PriceHistory.Count == 0);

    /// <summary>
    /// The usage example's unit choices for <paramref name="material"/>. Keeps the unit picked before
    /// (<paramref name="kept"/>, matched by id — the base unit's is null) when there was one; otherwise
    /// starts on its largest alternative unit, else its base unit.
    /// </summary>
    private void ReplaceExampleUnitOptions(RawMaterial? material, RawMaterialUnitOption? kept)
    {
        ExampleUnitOptions.Clear();
        ExampleUnitOptions.AddRange(RawMaterialUnitOption.For(material));
        ExampleUnit = (kept is null ? null : ExampleUnitOptions.FirstOrDefault(o => o.UnitId == kept.UnitId))
            ?? ExampleUnitOptions.Skip(1).FirstOrDefault()
            ?? ExampleUnitOptions.FirstOrDefault();
    }

    /// <summary>
    /// Puts the Purchase Cost Setup back to the latest price: its unit and amount, or the largest unit and
    /// no price. A unit added by picking it here goes, since it's no longer the purchase unit.
    /// </summary>
    private void ResetPurchaseSetup()
    {
        if (_purchaseAddedUnit is not null)
        {
            DropPurchaseAddedUnit();
            RebuildPurchaseUnitOptions();
        }

        SetPurchaseUnit(DefaultPurchaseUnit());
        EditingPrice = PriceHistory.FirstOrDefault()?.Entry.Price;
    }

    /// <summary>
    /// The latest price's unit, or for an unpriced material its largest unit, else the base unit. Never a
    /// new unit, which picking would add as an alternative unit. None for a new material, whose purchase
    /// unit starts empty, like its category (ClearForm).
    /// </summary>
    private RawMaterialUnitOption? DefaultPurchaseUnit()
    {
        if (SelectedRawMaterial is null)
        {
            return null;
        }

        var latest = PriceHistory.FirstOrDefault()?.Entry;
        var options = PurchaseUnitOptions.Where(o => !o.IsNew).ToList();
        return (latest is not null
                ? options.FirstOrDefault(o => o.UnitId == latest.UnitId)
                : options.Skip(1).FirstOrDefault())
            ?? options.FirstOrDefault();
    }

    /// <summary>
    /// Whether Save adds a price entry: the Purchase Cost Setup's unit or amount differs from the latest
    /// price, or an unpriced material has a price typed in. An unsaved unit's id is 0, which never
    /// matches the latest price's unit. An empty field on a priced material also counts, and is then
    /// rejected: a price goes by deleting its entry (PriceHistoryRules.IsNewPrice).
    /// </summary>
    private bool HasNewPrice()
    {
        if (EditingPurchaseUnit is null)
        {
            return false;
        }

        var latest = PriceHistory.FirstOrDefault()?.Entry;
        return latest is null
            ? PriceHistoryRules.IsNewPrice(null, EditingPrice)
            : latest.UnitId != EditingPurchaseUnit.UnitId || PriceHistoryRules.IsNewPrice(latest.Price, EditingPrice);
    }

    /// <summary>The BOM editor's lines when they differ from those last loaded, or null to leave the saved BOM as it is.</summary>
    private List<(int ComponentIngredientId, decimal Grams)>? ChangedBomLines()
    {
        var lines = CurrentBomLines();
        return lines.SequenceEqual(_savedBomLines) ? null : lines;
    }

    /// <summary>The BOM table's lines; a half-filled or repeated one is refused, naming the ingredient being made.</summary>
    private List<(int ComponentIngredientId, decimal Grams)> CurrentBomLines() =>
        [.. IngredientLineRow.CompleteLines(BomLineEdits, EditingName.Trim()).Select(l => (l.Ingredient.Id, l.Grams))];

    private async Task LoadBomLinesAsync(RawMaterial? material)
    {
        BomLineEdits.Clear();
        _savedBomLines = [];

        if (material is not { Source: RawMaterialSource.Made })
        {
            return;
        }

        var lines = await _bomLineRepository.GetForIngredientAsync(material.Id, CancellationToken.None);
        foreach (var line in lines)
        {
            BomLineEdits.Add(new IngredientLineRow(line.ComponentIngredient!, line.Grams));
        }

        _savedBomLines = CurrentBomLines();
    }

    private async Task LoadPriceHistoryAsync(RawMaterial? material)
    {
        PriceHistory.Clear();

        if (material is { Source: RawMaterialSource.Purchased })
        {
            var entries = await _priceHistoryRepository.GetForRawMaterialAsync(material.Id, CancellationToken.None);
            var baseUnitName = BaseUnitText.NameOf(material);
            PriceHistory.AddRange(entries.Select(e => new RawMaterialPriceRow(e, baseUnitName)));
        }

        LastUpdatedText = PriceHistory.FirstOrDefault()?.Date.ToString("MMM d, yyyy") ?? "—";
        UpdateCanChangeBaseUnit();
        ResetPurchaseSetup();
    }

    /// <summary>
    /// Re-fetches the material just saved and pushes it into the grid's cache and the selection. A fresh
    /// copy is always a new selection, so the form and its details reload from what was saved.
    /// </summary>
    private async Task ReloadSavedRawMaterialAsync(int id)
    {
        var saved = await _rawMaterialRepository.GetByIdAsync(id, CancellationToken.None);
        if (saved is not null)
        {
            _rawMaterialCache.AddOrUpdate(saved);
            SelectedRawMaterial = saved;
        }
    }
}
