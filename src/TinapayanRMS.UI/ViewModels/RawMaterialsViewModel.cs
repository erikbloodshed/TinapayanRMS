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

/// <summary>An in-memory (not yet saved) BOM line, shown in the BOM editor grid: an amount of a component in one of its units.</summary>
public class IngredientBomLineEdit(RawMaterial component, RawMaterialUnitOption unit, decimal quantity)
{
    public RawMaterial Component { get; } = component;

    public RawMaterialUnitOption Unit { get; } = unit;

    public decimal Quantity { get; } = quantity;

    public decimal Grams => UnitConversionRules.ToBaseQuantity(Quantity, Unit.Unit?.BaseUnitsPerUnit);
}

/// <summary>A row in the Alternative Units grid and a box in the Unit Conversion Overview.</summary>
public sealed class RawMaterialUnitRow(RawMaterialUnit unit, string baseUnitAbbreviation)
{
    public RawMaterialUnit Unit { get; } = unit;

    public string UnitName => Unit.UnitOfMeasure?.Name ?? "?";

    public string Abbreviation => Unit.UnitOfMeasure?.Abbreviation ?? "?";

    public decimal BaseUnitsPerUnit => Unit.BaseUnitsPerUnit;

    public string BaseEquivalentText => $"{Unit.BaseUnitsPerUnit:#,0.####} {baseUnitAbbreviation}";

    /// <summary>E.g. "1 bag = 25,000 g" or "1 roll = 1,000 cm".</summary>
    public string Example => $"1 {Abbreviation} = {BaseEquivalentText}";

    public string? Description => Unit.Description;
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

/// <summary>What the Usage Example worked out.</summary>
public sealed record UsageExampleResult(string Title, decimal QuantityInBaseUnits, string BaseUnitAbbreviation, decimal CostPerBaseUnit, decimal TotalCost);

/// <summary>
/// The Raw Materials screen: ingredients and packaging in one list. A material's type decides which
/// parts of its details apply — an ingredient is measured in grams and can be made from a BOM, a
/// packaging material has a base unit of its own and is always purchased.
/// </summary>
public class RawMaterialsViewModel : ViewModelBase
{
    private readonly IRawMaterialRepository _rawMaterialRepository;
    private readonly IRawMaterialUnitRepository _unitRepository;
    private readonly IRawMaterialPriceHistoryRepository _priceHistoryRepository;
    private readonly IIngredientBomLineRepository _bomLineRepository;
    private readonly IRawMaterialCategoryRepository _categoryRepository;
    private readonly IValidator<RawMaterial> _rawMaterialValidator;
    private readonly IValidator<RawMaterialUnit> _unitValidator;
    private readonly IValidator<RawMaterialPriceHistory> _priceHistoryValidator;
    private readonly SourceCache<RawMaterial, int> _rawMaterialCache = new(m => m.Id);
    private readonly ReadOnlyObservableCollection<RawMaterial> _rawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _visibleRawMaterials;
    private readonly ReadOnlyObservableCollection<RawMaterial> _ingredients;
    private readonly SourceCache<RawMaterialCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<RawMaterialCategory> _categories;

    /// <summary>Every raw material, for the usage example's picker.</summary>
    public ReadOnlyObservableCollection<RawMaterial> RawMaterials => _rawMaterials;

    /// <summary>The materials the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<RawMaterial> VisibleRawMaterials => _visibleRawMaterials;

    /// <summary>The ingredients only, for the BOM editor's component picker: packaging never goes in a recipe.</summary>
    public ReadOnlyObservableCollection<RawMaterial> Ingredients => _ingredients;

    public ObservableCollection<RawMaterialPriceRow> PriceHistory { get; } = [];

    public ObservableCollection<IngredientBomLineEdit> BomLineEdits { get; } = [];

    public ReadOnlyObservableCollection<RawMaterialCategory> Categories => _categories;

    /// <summary>Every unit of measure, for the base unit picker and the Alternative Units form.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

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
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
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
    /// A packaging material's base unit can be picked while it's new, or has no alternative units or
    /// prices yet (RawMaterialRules.EnsureBaseUnitCanChange; the repository also checks packaging lines).
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
    public bool IsEditingMade => _isEditingMade.Value;

    private readonly ObservableAsPropertyHelper<bool> _hasSelection;
    public bool HasSelection => _hasSelection.Value;

    private readonly ObservableAsPropertyHelper<bool> _showPurchaseSetup;
    public bool ShowPurchaseSetup => _showPurchaseSetup.Value;

    private readonly ObservableAsPropertyHelper<bool> _showBomEditor;
    public bool ShowBomEditor => _showBomEditor.Value;

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The summary header's big title: the material's name, or "New raw material".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string> _headerInitial;
    public string HeaderInitial => _headerInitial.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitName;
    /// <summary>The selected material's base unit name: "gram" for an ingredient, e.g. "centimeter" for ribbon.</summary>
    public string BaseUnitName => _baseUnitName.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitAbbreviation;
    public string BaseUnitAbbreviation => _baseUnitAbbreviation.Value;

    private readonly ObservableAsPropertyHelper<string> _currentCostText;
    /// <summary>E.g. "₱0.0372 / g", or "—" with nothing selected.</summary>
    public string CurrentCostText => _currentCostText.Value;

    /// <summary>The latest price's date, or "—" for an unpriced or made material.</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    // Alternative Units: the row picked in the grid (Edit and Remove act on it) and the add/edit form.
    private RawMaterialUnitRow? _selectedUnitRow;
    public RawMaterialUnitRow? SelectedUnitRow
    {
        get => _selectedUnitRow;
        set => this.RaiseAndSetIfChanged(ref _selectedUnitRow, value);
    }

    /// <summary>Whether the selected material can take another alternative unit (UnitConversionRules.MaxAlternativeUnits).</summary>
    private bool _canAddUnit;
    public bool CanAddUnit
    {
        get => _canAddUnit;
        private set => this.RaiseAndSetIfChanged(ref _canAddUnit, value);
    }

    private int _editingUnitId;

    private bool _isUnitFormOpen;
    public bool IsUnitFormOpen
    {
        get => _isUnitFormOpen;
        private set => this.RaiseAndSetIfChanged(ref _isUnitFormOpen, value);
    }

    private string _unitFormTitle = "Add Alternative Unit";
    public string UnitFormTitle
    {
        get => _unitFormTitle;
        private set => this.RaiseAndSetIfChanged(ref _unitFormTitle, value);
    }

    private UnitOfMeasure? _editingUnitOfMeasure;
    public UnitOfMeasure? EditingUnitOfMeasure
    {
        get => _editingUnitOfMeasure;
        set => this.RaiseAndSetIfChanged(ref _editingUnitOfMeasure, value);
    }

    private decimal? _editingUnitBaseUnitsPerUnit;
    public decimal? EditingUnitBaseUnitsPerUnit
    {
        get => _editingUnitBaseUnitsPerUnit;
        set => this.RaiseAndSetIfChanged(ref _editingUnitBaseUnitsPerUnit, value);
    }

    private string _editingUnitDescription = string.Empty;
    public string EditingUnitDescription
    {
        get => _editingUnitDescription;
        set => this.RaiseAndSetIfChanged(ref _editingUnitDescription, value);
    }

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

    private readonly ObservableAsPropertyHelper<string> _purchaseCostText;
    /// <summary>What EditingPrice comes to per base unit, recalculated as it's typed, e.g. "₱0.0372 / g".</summary>
    public string PurchaseCostText => _purchaseCostText.Value;

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

    // Bill of Materials (made ingredients).
    private RawMaterial? _editingBomComponent;
    public RawMaterial? EditingBomComponent
    {
        get => _editingBomComponent;
        set => this.RaiseAndSetIfChanged(ref _editingBomComponent, value);
    }

    /// <summary>The picked component's units, for the BOM editor's unit picker.</summary>
    public ObservableCollection<RawMaterialUnitOption> BomUnitOptions { get; } = [];

    private RawMaterialUnitOption? _editingBomUnit;
    public RawMaterialUnitOption? EditingBomUnit
    {
        get => _editingBomUnit;
        set => this.RaiseAndSetIfChanged(ref _editingBomUnit, value);
    }

    private decimal _editingBomQuantity;
    public decimal EditingBomQuantity
    {
        get => _editingBomQuantity;
        set => this.RaiseAndSetIfChanged(ref _editingBomQuantity, value);
    }

    private IngredientBomLineEdit? _selectedBomLineEdit;
    public IngredientBomLineEdit? SelectedBomLineEdit
    {
        get => _selectedBomLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedBomLineEdit, value);
    }

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
    public ReactiveCommand<RxVoid, RxVoid> AddUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelUnitCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SavePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ResetPriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeletePriceCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddBomLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveBomLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveBomCommand { get; }

    public RawMaterialsViewModel(
        IRawMaterialRepository rawMaterialRepository,
        IRawMaterialUnitRepository unitRepository,
        IRawMaterialPriceHistoryRepository priceHistoryRepository,
        IIngredientBomLineRepository bomLineRepository,
        IRawMaterialCategoryRepository categoryRepository,
        IValidator<RawMaterial> rawMaterialValidator,
        IValidator<RawMaterialUnit> unitValidator,
        IValidator<RawMaterialPriceHistory> priceHistoryValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        ILogger logger)
        : base(logger)
    {
        _rawMaterialRepository = rawMaterialRepository;
        _unitRepository = unitRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _bomLineRepository = bomLineRepository;
        _categoryRepository = categoryRepository;
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

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<RawMaterialCategory>.Ascending(c => c.Name))
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

        _hasSelection = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is not null)
            .ToProperty(this, x => x.HasSelection);

        _showPurchaseSetup = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is { Source: RawMaterialSource.Purchased })
            .ToProperty(this, x => x.ShowPurchaseSetup);

        _showBomEditor = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is { Source: RawMaterialSource.Made })
            .ToProperty(this, x => x.ShowBomEditor);

        _headerTitle = this.WhenAnyValue(x => x.SelectedRawMaterial, x => x.EditingName, (material, name) =>
                material is null && string.IsNullOrWhiteSpace(name) ? "New raw material" : name)
            .ToProperty(this, x => x.HeaderTitle, "New raw material");

        _headerInitial = this.WhenAnyValue(x => x.HeaderTitle)
            .Select(title => string.IsNullOrWhiteSpace(title) ? "?" : title.Trim()[..1].ToUpperInvariant())
            .ToProperty(this, x => x.HeaderInitial, "N");

        _baseUnitName = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(BaseUnitText.NameOf)
            .ToProperty(this, x => x.BaseUnitName, "gram");

        _baseUnitAbbreviation = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(BaseUnitText.AbbreviationOf)
            .ToProperty(this, x => x.BaseUnitAbbreviation, "g");

        _currentCostText = this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Select(material => material is null ? "—" : $"₱{material.CostPerBaseUnit:N4} / {BaseUnitText.AbbreviationOf(material)}")
            .ToProperty(this, x => x.CurrentCostText, "—");

        _purchaseCostText = this.WhenAnyValue(x => x.EditingPrice, x => x.EditingPurchaseUnit, x => x.BaseUnitAbbreviation, (price, unit, abbreviation) =>
                price is > 0 && unit is not null
                    ? $"₱{UnitConversionRules.CostPerBaseUnit(price.Value, unit.BaseUnitsPerUnit):N4} / {abbreviation}"
                    : "—")
            .ToProperty(this, x => x.PurchaseCostText, "—");

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
                }
                else
                {
                    EditingBaseUnit = null;
                }
            });

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which
        // only re-points a selection at its refreshed copy; the form and details must stay as-is.
        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(material => material is not null && !IsRefreshing)
            .Subscribe(material => LoadIntoForm(material!));

        this.WhenAnyValue(x => x.SelectedRawMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material =>
            {
                SelectedPriceRow = null;
                SelectedBomLineEdit = null;
                EditingBomComponent = null;
                EditingBomQuantity = 0m;
                CloseUnitForm();
                RebuildUnitViews(material);
                if (material is not null)
                {
                    ExampleRawMaterial = material;
                }
            });

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

        this.WhenAnyValue(x => x.ExampleRawMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material => ReplaceExampleUnitOptions(material, kept: null));

        this.WhenAnyValue(x => x.EditingBomComponent)
            .Where(_ => !IsRefreshing)
            .Subscribe(component => ReplaceUnitOptions(BomUnitOptions, component, unit => EditingBomUnit = unit, keepUnitId: null));

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
            var baseUnitId = EditingBaseUnit?.Id;
            var unitOfMeasureId = EditingUnitOfMeasure?.Id;
            var bomComponentId = EditingBomComponent?.Id;
            var bomUnitId = EditingBomUnit?.UnitId;
            var exampleMaterialId = ExampleRawMaterial?.Id;
            var exampleUnit = ExampleUnit;
            var purchaseUnitId = EditingPurchaseUnit?.UnitId;
            var selectedUnitId = SelectedUnitRow?.Unit.Id;

            ApplyRefresh(() =>
            {
                _categoryCache.MergeFrom(categories);
                UnitsOfMeasure.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
                _rawMaterialCache.MergeFrom(rawMaterials);

                EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
                EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId);
                EditingUnitOfMeasure = UnitsOfMeasure.FirstOrDefault(u => u.Id == unitOfMeasureId);

                var previousBomComponent = EditingBomComponent;
                EditingBomComponent = Ingredients.FirstOrDefault(m => m.Id == bomComponentId);
                if (!ReferenceEquals(previousBomComponent, EditingBomComponent))
                {
                    ReplaceUnitOptions(BomUnitOptions, EditingBomComponent, unit => EditingBomUnit = unit, keepUnitId: bomUnitId);
                }

                var previouslySelected = SelectedRawMaterial;
                SelectedRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == selectedId);
                if (!ReferenceEquals(previouslySelected, SelectedRawMaterial) && SelectedRawMaterial is not null)
                {
                    // Its units changed (a unit renamed in Settings): rebuild what shows them,
                    // keeping the purchase unit picked.
                    RebuildUnitViews(SelectedRawMaterial);
                    EditingPurchaseUnit = PurchaseUnitOptions.FirstOrDefault(o => o.UnitId == purchaseUnitId);
                    SelectedUnitRow = UnitRows.FirstOrDefault(r => r.Unit.Id == selectedUnitId);
                }

                var previousExample = ExampleRawMaterial;
                ExampleRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == exampleMaterialId);
                if (!ReferenceEquals(previousExample, ExampleRawMaterial))
                {
                    ReplaceExampleUnitOptions(ExampleRawMaterial, exampleUnit);
                    ExampleResult = null;
                }
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedRawMaterial = null;
            _editingId = 0;
            CanChangeTypeAndSource = true;
            EditingName = string.Empty;
            EditingType = RawMaterialType.Ingredient;
            EditingCategory = Categories.FirstOrDefault();
            EditingSource = RawMaterialSource.Purchased;
            EditingWeightLossPercent = null;
            EditingBaseUnit = null;
        });

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
                WeightLossPercent = source == RawMaterialSource.Made ? EditingWeightLossPercent : null,
                BaseUnitId = type == RawMaterialType.Packaging ? EditingBaseUnit?.Id : null,
            };

            var validation = _rawMaterialValidator.Validate(material);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _rawMaterialRepository.AddAsync(material, CancellationToken.None);
                await LoadCommand.Execute();

                // Straight into the new material, so its units and price can be added next.
                SelectedRawMaterial = RawMaterials.FirstOrDefault(m => m.Id == material.Id);
                return;
            }

            await _rawMaterialRepository.UpdateAsync(material, CancellationToken.None);
            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedRawMaterial!.Name}'? This can't be undone.", async () =>
            {
                await _rawMaterialRepository.DeleteAsync(SelectedRawMaterial!.Id, CancellationToken.None);
                SelectedRawMaterial = null;
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedRawMaterial).Select(material => material is not null));

        AddUnitCommand = ReactiveCommand.Create(
            () =>
            {
                _editingUnitId = 0;
                UnitFormTitle = "Add Alternative Unit";
                EditingUnitOfMeasure = null;
                EditingUnitBaseUnitsPerUnit = null;
                EditingUnitDescription = string.Empty;
                IsUnitFormOpen = true;
            },
            this.WhenAnyValue(x => x.CanAddUnit));

        var hasSelectedUnitRow = this.WhenAnyValue(x => x.SelectedUnitRow).Select(row => row is not null);

        EditUnitCommand = ReactiveCommand.Create(
            () =>
            {
                var row = SelectedUnitRow!;
                _editingUnitId = row.Unit.Id;
                UnitFormTitle = "Edit Alternative Unit";
                EditingUnitOfMeasure = UnitsOfMeasure.FirstOrDefault(u => u.Id == row.Unit.UnitOfMeasureId);
                EditingUnitBaseUnitsPerUnit = row.Unit.BaseUnitsPerUnit;
                EditingUnitDescription = row.Unit.Description ?? string.Empty;
                IsUnitFormOpen = true;
            },
            hasSelectedUnitRow);

        DeleteUnitCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Remove '{SelectedUnitRow!.UnitName}' from '{SelectedRawMaterial?.Name}'?", async () =>
            {
                await _unitRepository.DeleteAsync(SelectedUnitRow!.Unit.Id, CancellationToken.None);
                await RefreshSelectedRawMaterialAsync();
            }),
            hasSelectedUnitRow);

        SaveUnitCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var unit = new RawMaterialUnit
            {
                Id = _editingUnitId,
                RawMaterialId = SelectedRawMaterial!.Id,
                UnitOfMeasureId = EditingUnitOfMeasure?.Id ?? 0,
                BaseUnitsPerUnit = EditingUnitBaseUnitsPerUnit ?? 0m,
                Description = string.IsNullOrWhiteSpace(EditingUnitDescription) ? null : EditingUnitDescription.Trim(),
            };

            var validation = _unitValidator.Validate(unit);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingUnitId == 0)
            {
                await _unitRepository.AddAsync(unit, CancellationToken.None);
            }
            else
            {
                await _unitRepository.UpdateAsync(unit, CancellationToken.None);
            }

            CloseUnitForm();
            await RefreshSelectedRawMaterialAsync();
        }));

        CancelUnitCommand = ReactiveCommand.Create(CloseUnitForm);

        SavePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var latest = PriceHistory.FirstOrDefault()?.Entry;
                var unitId = EditingPurchaseUnit?.UnitId;
                if (latest is not null && latest.UnitId == unitId && !PriceHistoryRules.IsNewPrice(latest.Price, EditingPrice))
                {
                    return; // Nothing changed.
                }

                var entry = new RawMaterialPriceHistory
                {
                    RawMaterialId = SelectedRawMaterial!.Id,
                    Date = DateOnly.FromDateTime(DateTime.Today),
                    UnitId = unitId,
                    Price = EditingPrice ?? 0m,
                };

                var validation = _priceHistoryValidator.Validate(entry);
                if (!validation.IsValid)
                {
                    ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                    return;
                }

                await _priceHistoryRepository.AddAsync(entry, CancellationToken.None);
                await RefreshSelectedRawMaterialAsync();
            }),
            this.WhenAnyValue(x => x.SelectedRawMaterial, x => x.EditingPurchaseUnit,
                (material, unit) => material is { Source: RawMaterialSource.Purchased } && unit is not null));

        ResetPriceCommand = ReactiveCommand.Create(ResetPurchaseSetup);

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
                    baseQuantity * material.CostPerBaseUnit);
            },
            this.WhenAnyValue(x => x.ExampleRawMaterial, x => x.ExampleUnit, x => x.ExampleQuantity,
                (material, unit, quantity) => material is not null && unit is not null && quantity > 0));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price entry? This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceRow!.Entry.Id, CancellationToken.None);
                await RefreshSelectedRawMaterialAsync();
            }),
            this.WhenAnyValue(x => x.SelectedPriceRow).Select(row => row is not null));

        AddBomLineCommand = ReactiveCommand.Create(
            () => BomLineEdits.Add(new IngredientBomLineEdit(EditingBomComponent!, EditingBomUnit!, EditingBomQuantity)),
            this.WhenAnyValue(x => x.EditingBomComponent, x => x.EditingBomUnit, x => x.EditingBomQuantity,
                (component, unit, quantity) => component is not null && unit is not null && quantity > 0));

        RemoveBomLineCommand = ReactiveCommand.Create(
            () => { BomLineEdits.Remove(SelectedBomLineEdit!); },
            this.WhenAnyValue(x => x.SelectedBomLineEdit).Select(line => line is not null));

        SaveBomCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var lines = BomLineEdits.Select(l => (l.Component.Id, l.Unit.UnitId, l.Quantity)).ToList();
                await _bomLineRepository.SetLinesAsync(SelectedRawMaterial!.Id, lines, CancellationToken.None);
                await RefreshSelectedRawMaterialAsync();
            }),
            this.WhenAnyValue(x => x.SelectedRawMaterial).Select(material => material is { Source: RawMaterialSource.Made }));
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
        EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == material.BaseUnitId);
    }

    /// <summary>The Alternative Units rows and the purchase unit choices, from the material's own units.</summary>
    private void RebuildUnitViews(RawMaterial? material)
    {
        SelectedUnitRow = null;
        UnitRows.Clear();
        var baseUnitAbbreviation = BaseUnitText.AbbreviationOf(material);
        UnitRows.AddRange((material?.Units ?? []).Select(u => new RawMaterialUnitRow(u, baseUnitAbbreviation)));
        CanAddUnit = material is not null && UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;

        PurchaseUnitOptions.Clear();
        PurchaseUnitOptions.AddRange(RawMaterialUnitOption.For(material));

        UpdateCanChangeBaseUnit();
    }

    private void UpdateCanChangeBaseUnit() =>
        CanChangeBaseUnit = SelectedRawMaterial is null || (UnitRows.Count == 0 && PriceHistory.Count == 0);

    /// <summary>
    /// Fills a unit picker with <paramref name="material"/>'s units and picks the one with
    /// <paramref name="keepUnitId"/>, falling back to the base unit.
    /// </summary>
    private static void ReplaceUnitOptions(
        ObservableCollection<RawMaterialUnitOption> options, RawMaterial? material, Action<RawMaterialUnitOption?> pick, int? keepUnitId)
    {
        pick(null);
        options.Clear();
        if (material is null)
        {
            return;
        }

        options.AddRange(RawMaterialUnitOption.For(material));
        pick(options.FirstOrDefault(o => o.UnitId == keepUnitId) ?? options[0]);
    }

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

    private void CloseUnitForm()
    {
        IsUnitFormOpen = false;
        _editingUnitId = 0;
    }

    /// <summary>Puts the Purchase Cost Setup back to the latest price: its unit and amount, or the largest unit and no price.</summary>
    private void ResetPurchaseSetup()
    {
        var latest = PriceHistory.FirstOrDefault()?.Entry;
        EditingPurchaseUnit = latest is not null
            ? PurchaseUnitOptions.FirstOrDefault(o => o.UnitId == latest.UnitId)
            : PurchaseUnitOptions.Skip(1).FirstOrDefault() ?? PurchaseUnitOptions.FirstOrDefault();
        EditingPrice = latest?.Price;
    }

    private async Task LoadBomLinesAsync(RawMaterial? material)
    {
        BomLineEdits.Clear();

        if (material is not { Source: RawMaterialSource.Made })
        {
            return;
        }

        var lines = await _bomLineRepository.GetForIngredientAsync(material.Id, CancellationToken.None);
        foreach (var line in lines)
        {
            BomLineEdits.Add(new IngredientBomLineEdit(line.ComponentIngredient!, new RawMaterialUnitOption(line.Unit, line.ComponentIngredient!), line.Quantity));
        }
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
    /// Re-fetches the selected material (its units or CostPerBaseUnit just changed) and pushes it
    /// back into the grid's cache and the selection, which also reloads its details.
    /// </summary>
    private async Task RefreshSelectedRawMaterialAsync()
    {
        if (SelectedRawMaterial is null)
        {
            return;
        }

        var refreshed = await _rawMaterialRepository.GetByIdAsync(SelectedRawMaterial.Id, CancellationToken.None);
        if (refreshed is not null)
        {
            _rawMaterialCache.AddOrUpdate(refreshed);
            SelectedRawMaterial = refreshed;
        }
    }
}
