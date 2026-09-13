using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>A row in the packaging Alternative Units grid and a box in the Unit Conversion Overview.</summary>
public sealed class PackagingMaterialUnitRow(PackagingMaterialUnit unit, UnitOfMeasure? baseUnit)
{
    public PackagingMaterialUnit Unit { get; } = unit;

    public string UnitName => Unit.UnitOfMeasure?.Name ?? "?";

    public string Abbreviation => Unit.UnitOfMeasure?.Abbreviation ?? "?";

    public decimal BaseUnitsPerUnit => Unit.BaseUnitsPerUnit;

    public string BaseEquivalentText => $"{Unit.BaseUnitsPerUnit:#,0.####} {baseUnit?.Abbreviation}";

    /// <summary>E.g. "1 roll = 1,000 cm".</summary>
    public string Example => $"1 {Abbreviation} = {BaseEquivalentText}";

    public string? Description => Unit.Description;
}

/// <summary>A row in the packaging Price History grid: the entry, the unit it was priced in, and what that comes to per base unit.</summary>
public sealed class PackagingPriceRow(PackagingPriceHistory entry, UnitOfMeasure? baseUnit)
{
    public PackagingPriceHistory Entry { get; } = entry;

    public DateOnly Date => Entry.Date;

    public string UnitName => Entry.PackagingMaterialUnit?.UnitOfMeasure?.Name ?? baseUnit?.Name ?? "?";

    public decimal Price => Entry.Price;

    public decimal CostPerBaseUnit => UnitConversionRules.CostPerBaseUnit(Entry.Price, Entry.PackagingMaterialUnit?.BaseUnitsPerUnit);
}

/// <summary>What the packaging Usage Example worked out.</summary>
public sealed record PackagingUsageExampleResult(string Title, decimal QuantityInBaseUnits, string BaseUnitAbbreviation, decimal CostPerBaseUnit, decimal TotalCost);

public class PackagingMaterialsViewModel : ViewModelBase
{
    private readonly IPackagingMaterialRepository _packagingMaterialRepository;
    private readonly IPackagingMaterialUnitRepository _unitRepository;
    private readonly IPackagingPriceHistoryRepository _priceHistoryRepository;
    private readonly IPackagingCategoryRepository _categoryRepository;
    private readonly IValidator<PackagingMaterial> _packagingMaterialValidator;
    private readonly IValidator<PackagingMaterialUnit> _unitValidator;
    private readonly IValidator<PackagingPriceHistory> _priceHistoryValidator;
    private readonly IValidator<PackagingCategory> _categoryValidator;
    private readonly SourceCache<PackagingMaterial, int> _packagingMaterialCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<PackagingMaterial> _packagingMaterials;
    private readonly ReadOnlyObservableCollection<PackagingMaterial> _visiblePackagingMaterials;
    private readonly SourceCache<PackagingCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<PackagingCategory> _categories;

    /// <summary>Every packaging material, for the usage example's picker.</summary>
    public ReadOnlyObservableCollection<PackagingMaterial> PackagingMaterials => _packagingMaterials;

    /// <summary>The materials the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<PackagingMaterial> VisiblePackagingMaterials => _visiblePackagingMaterials;

    public ObservableCollection<PackagingPriceRow> PriceHistory { get; } = [];

    public ReadOnlyObservableCollection<PackagingCategory> Categories => _categories;

    /// <summary>Every unit of measure, for the base unit picker and the Alternative Units form.</summary>
    public ObservableCollection<UnitOfMeasure> UnitsOfMeasure { get; } = [];

    public ObservableCollection<PackagingMaterialUnitRow> UnitRows { get; } = [];

    public ObservableCollection<PackagingUnitOption> PurchaseUnitOptions { get; } = [];

    public ObservableCollection<PackagingUnitOption> ExampleUnitOptions { get; } = [];

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    private PackagingMaterial? _selectedPackagingMaterial;
    public PackagingMaterial? SelectedPackagingMaterial
    {
        get => _selectedPackagingMaterial;
        set => this.RaiseAndSetIfChanged(ref _selectedPackagingMaterial, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private PackagingCategory? _editingCategory;
    public PackagingCategory? EditingCategory
    {
        get => _editingCategory;
        set => this.RaiseAndSetIfChanged(ref _editingCategory, value);
    }

    private UnitOfMeasure? _editingBaseUnit;
    public UnitOfMeasure? EditingBaseUnit
    {
        get => _editingBaseUnit;
        set => this.RaiseAndSetIfChanged(ref _editingBaseUnit, value);
    }

    /// <summary>
    /// The base unit can be picked for a new material, or one with no alternative units or prices yet
    /// (PackagingMaterialRules.EnsureBaseUnitCanChange; the repository also checks packaging lines).
    /// </summary>
    private bool _canChangeBaseUnit = true;
    public bool CanChangeBaseUnit
    {
        get => _canChangeBaseUnit;
        private set => this.RaiseAndSetIfChanged(ref _canChangeBaseUnit, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _hasSelection;
    public bool HasSelection => _hasSelection.Value;

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The summary header's big title: the material's name, or "New packaging material".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string> _headerInitial;
    public string HeaderInitial => _headerInitial.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitName;
    /// <summary>The selected material's base unit name, e.g. "centimeter".</summary>
    public string BaseUnitName => _baseUnitName.Value;

    private readonly ObservableAsPropertyHelper<string> _baseUnitAbbreviation;
    public string BaseUnitAbbreviation => _baseUnitAbbreviation.Value;

    private readonly ObservableAsPropertyHelper<string> _currentCostText;
    /// <summary>E.g. "₱0.0500 / cm", or "—" with nothing selected.</summary>
    public string CurrentCostText => _currentCostText.Value;

    /// <summary>The latest price's date, or "—" for an unpriced material.</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    // Alternative Units: the row picked in the grid (Edit and Remove act on it) and the add/edit form.
    private PackagingMaterialUnitRow? _selectedUnitRow;
    public PackagingMaterialUnitRow? SelectedUnitRow
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
    private PackagingUnitOption? _editingPurchaseUnit;
    public PackagingUnitOption? EditingPurchaseUnit
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
    /// <summary>What EditingPrice comes to per base unit, recalculated as it's typed, e.g. "₱0.0450 / cm".</summary>
    public string PurchaseCostText => _purchaseCostText.Value;

    // Usage Example.
    private PackagingMaterial? _exampleMaterial;
    public PackagingMaterial? ExampleMaterial
    {
        get => _exampleMaterial;
        set => this.RaiseAndSetIfChanged(ref _exampleMaterial, value);
    }

    private decimal _exampleQuantity = 1m;
    public decimal ExampleQuantity
    {
        get => _exampleQuantity;
        set => this.RaiseAndSetIfChanged(ref _exampleQuantity, value);
    }

    private PackagingUnitOption? _exampleUnit;
    public PackagingUnitOption? ExampleUnit
    {
        get => _exampleUnit;
        set => this.RaiseAndSetIfChanged(ref _exampleUnit, value);
    }

    private PackagingUsageExampleResult? _exampleResult;
    public PackagingUsageExampleResult? ExampleResult
    {
        get => _exampleResult;
        private set => this.RaiseAndSetIfChanged(ref _exampleResult, value);
    }

    private PackagingPriceRow? _selectedPriceRow;
    public PackagingPriceRow? SelectedPriceRow
    {
        get => _selectedPriceRow;
        set => this.RaiseAndSetIfChanged(ref _selectedPriceRow, value);
    }

    // Category management (embedded panel, independent of the selected packaging material).
    private PackagingCategory? _selectedCategory;
    public PackagingCategory? SelectedCategory
    {
        get => _selectedCategory;
        set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
    }

    private int _editingCategoryId;

    private string _editingCategoryName = string.Empty;
    public string EditingCategoryName
    {
        get => _editingCategoryName;
        set => this.RaiseAndSetIfChanged(ref _editingCategoryName, value);
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
    public ReactiveCommand<RxVoid, RxVoid> NewCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCategoryCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCategoryCommand { get; }

    public PackagingMaterialsViewModel(
        IPackagingMaterialRepository packagingMaterialRepository,
        IPackagingMaterialUnitRepository unitRepository,
        IPackagingPriceHistoryRepository priceHistoryRepository,
        IPackagingCategoryRepository categoryRepository,
        IValidator<PackagingMaterial> packagingMaterialValidator,
        IValidator<PackagingMaterialUnit> unitValidator,
        IValidator<PackagingPriceHistory> priceHistoryValidator,
        IValidator<PackagingCategory> categoryValidator,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        ILogger logger)
        : base(logger)
    {
        _packagingMaterialRepository = packagingMaterialRepository;
        _unitRepository = unitRepository;
        _priceHistoryRepository = priceHistoryRepository;
        _categoryRepository = categoryRepository;
        _packagingMaterialValidator = packagingMaterialValidator;
        _unitValidator = unitValidator;
        _priceHistoryValidator = priceHistoryValidator;
        _categoryValidator = categoryValidator;

        var byName = SortExpressionComparer<PackagingMaterial>.Ascending(p => p.Name);

        _packagingMaterialCache.Connect()
            .SortAndBind(out _packagingMaterials, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<PackagingMaterial, bool>)(m => text.Length == 0 || m.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        _packagingMaterialCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visiblePackagingMaterials, byName)
            .Subscribe();

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<PackagingCategory>.Ascending(c => c.Name))
            .Subscribe();

        _hasSelection = this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Select(material => material is not null)
            .ToProperty(this, x => x.HasSelection);

        _headerTitle = this.WhenAnyValue(x => x.SelectedPackagingMaterial, x => x.EditingName, (material, name) =>
                material is null && string.IsNullOrWhiteSpace(name) ? "New packaging material" : name)
            .ToProperty(this, x => x.HeaderTitle, "New packaging material");

        _headerInitial = this.WhenAnyValue(x => x.HeaderTitle)
            .Select(title => string.IsNullOrWhiteSpace(title) ? "?" : title.Trim()[..1].ToUpperInvariant())
            .ToProperty(this, x => x.HeaderInitial, "N");

        _baseUnitName = this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Select(material => material?.BaseUnit?.Name ?? "base unit")
            .ToProperty(this, x => x.BaseUnitName, "base unit");

        _baseUnitAbbreviation = this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Select(material => material?.BaseUnit?.Abbreviation ?? string.Empty)
            .ToProperty(this, x => x.BaseUnitAbbreviation, string.Empty);

        _currentCostText = this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Select(material => material is null ? "—" : $"₱{material.CostPerBaseUnit:N4} / {material.BaseUnit?.Abbreviation}")
            .ToProperty(this, x => x.CurrentCostText, "—");

        _purchaseCostText = this.WhenAnyValue(x => x.EditingPrice, x => x.EditingPurchaseUnit, x => x.BaseUnitAbbreviation, (price, unit, abbreviation) =>
                price is > 0 && unit is not null
                    ? $"₱{UnitConversionRules.CostPerBaseUnit(price.Value, unit.BaseUnitsPerUnit):N4} / {abbreviation}"
                    : "—")
            .ToProperty(this, x => x.PurchaseCostText, "—");

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which
        // only re-points a selection at its refreshed copy; the form and details must stay as-is.
        this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Where(material => material is not null && !IsRefreshing)
            .Subscribe(material => LoadIntoForm(material!));

        this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material =>
            {
                SelectedPriceRow = null;
                CloseUnitForm();
                RebuildUnitViews(material);
                if (material is not null)
                {
                    ExampleMaterial = material;
                }
            });

        this.WhenAnyValue(x => x.SelectedPackagingMaterial)
            .Where(_ => !IsRefreshing)
            .Select(material => Observable.FromAsync(() => LoadPriceHistoryAsync(material)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.ExampleMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material =>
            {
                ExampleUnitOptions.Clear();
                ExampleUnitOptions.AddRange(PackagingUnitOption.For(material));
                ExampleUnit = ExampleUnitOptions.FirstOrDefault();
            });

        // A worked-out example belongs to the inputs it was worked out from.
        this.WhenAnyValue(x => x.ExampleMaterial, x => x.ExampleQuantity, x => x.ExampleUnit)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ => ExampleResult = null);

        this.WhenAnyValue(x => x.SelectedCategory)
            .Where(category => category is not null && !IsRefreshing)
            .Subscribe(category =>
            {
                _editingCategoryId = category!.Id;
                EditingCategoryName = category.Name;
            });

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, picker choices and unsaved edits
        // while picking up what changed elsewhere, such as a unit renamed on another tab.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            var packagingMaterials = await _packagingMaterialRepository.GetAllAsync(CancellationToken.None);

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedPackagingMaterial?.Id;
            var categoryId = EditingCategory?.Id;
            var panelCategoryId = SelectedCategory?.Id;
            var baseUnitId = EditingBaseUnit?.Id;
            var unitOfMeasureId = EditingUnitOfMeasure?.Id;
            var purchaseUnitId = EditingPurchaseUnit?.UnitId;
            var exampleMaterialId = ExampleMaterial?.Id;
            var exampleUnitId = ExampleUnit?.UnitId;
            var selectedUnitId = SelectedUnitRow?.Unit.Id;

            ApplyRefresh(() =>
            {
                _categoryCache.MergeFrom(categories);
                UnitsOfMeasure.MergeFrom([.. units.OrderBy(u => u.Name)], u => u.Id);
                _packagingMaterialCache.MergeFrom(packagingMaterials);

                EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
                SelectedCategory = Categories.FirstOrDefault(c => c.Id == panelCategoryId);
                EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == baseUnitId);
                EditingUnitOfMeasure = UnitsOfMeasure.FirstOrDefault(u => u.Id == unitOfMeasureId);

                var previouslySelected = SelectedPackagingMaterial;
                SelectedPackagingMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == selectedId);
                if (!ReferenceEquals(previouslySelected, SelectedPackagingMaterial) && SelectedPackagingMaterial is not null)
                {
                    // Its units changed (a unit renamed on another tab): rebuild what shows them,
                    // keeping the purchase unit picked.
                    RebuildUnitViews(SelectedPackagingMaterial);
                    EditingPurchaseUnit = PurchaseUnitOptions.FirstOrDefault(o => o.UnitId == purchaseUnitId);
                    SelectedUnitRow = UnitRows.FirstOrDefault(r => r.Unit.Id == selectedUnitId);
                }

                var previousExample = ExampleMaterial;
                ExampleMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == exampleMaterialId);
                if (!ReferenceEquals(previousExample, ExampleMaterial))
                {
                    ExampleUnitOptions.Clear();
                    ExampleUnitOptions.AddRange(PackagingUnitOption.For(ExampleMaterial));
                    ExampleUnit = ExampleUnitOptions.FirstOrDefault(o => o.UnitId == exampleUnitId) ?? ExampleUnitOptions.FirstOrDefault();
                    ExampleResult = null;
                }
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedPackagingMaterial = null;
            _editingId = 0;
            CanChangeBaseUnit = true;
            EditingName = string.Empty;
            EditingCategory = Categories.FirstOrDefault();
            EditingBaseUnit = null;
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var packagingMaterial = new PackagingMaterial
            {
                Id = _editingId,
                Name = EditingName,
                PackagingCategoryId = EditingCategory?.Id ?? 0,
                BaseUnitId = EditingBaseUnit?.Id ?? 0,
            };

            var validation = _packagingMaterialValidator.Validate(packagingMaterial);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _packagingMaterialRepository.AddAsync(packagingMaterial, CancellationToken.None);
                await LoadCommand.Execute();

                // Straight into the new material, so its units and price can be added next.
                SelectedPackagingMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == packagingMaterial.Id);
                return;
            }

            await _packagingMaterialRepository.UpdateAsync(packagingMaterial, CancellationToken.None);
            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedPackagingMaterial!.Name}'? This can't be undone.", async () =>
            {
                await _packagingMaterialRepository.DeleteAsync(SelectedPackagingMaterial!.Id, CancellationToken.None);
                SelectedPackagingMaterial = null;
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedPackagingMaterial).Select(material => material is not null));

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
            () => RunWithConfirmationAsync($"Remove '{SelectedUnitRow!.UnitName}' from '{SelectedPackagingMaterial?.Name}'?", async () =>
            {
                await _unitRepository.DeleteAsync(SelectedUnitRow!.Unit.Id, CancellationToken.None);
                await RefreshSelectedPackagingMaterialAsync();
            }),
            hasSelectedUnitRow);

        SaveUnitCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var unit = new PackagingMaterialUnit
            {
                Id = _editingUnitId,
                PackagingMaterialId = SelectedPackagingMaterial!.Id,
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
            await RefreshSelectedPackagingMaterialAsync();
        }));

        CancelUnitCommand = ReactiveCommand.Create(CloseUnitForm);

        SavePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var latest = PriceHistory.FirstOrDefault()?.Entry;
                var unitId = EditingPurchaseUnit?.UnitId;
                if (latest is not null && latest.PackagingMaterialUnitId == unitId && !PriceHistoryRules.IsNewPrice(latest.Price, EditingPrice))
                {
                    return; // Nothing changed.
                }

                var entry = new PackagingPriceHistory
                {
                    PackagingMaterialId = SelectedPackagingMaterial!.Id,
                    Date = DateOnly.FromDateTime(DateTime.Today),
                    PackagingMaterialUnitId = unitId,
                    Price = EditingPrice ?? 0m,
                };

                var validation = _priceHistoryValidator.Validate(entry);
                if (!validation.IsValid)
                {
                    ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                    return;
                }

                await _priceHistoryRepository.AddAsync(entry, CancellationToken.None);
                await RefreshSelectedPackagingMaterialAsync();
            }),
            this.WhenAnyValue(x => x.SelectedPackagingMaterial, x => x.EditingPurchaseUnit,
                (material, unit) => material is not null && unit is not null));

        ResetPriceCommand = ReactiveCommand.Create(ResetPurchaseSetup);

        CalculateExampleCommand = ReactiveCommand.Create(
            () =>
            {
                var material = ExampleMaterial!;
                var unit = ExampleUnit!;
                var baseQuantity = UnitConversionRules.ToBaseQuantity(ExampleQuantity, unit.Unit?.BaseUnitsPerUnit);
                ExampleResult = new PackagingUsageExampleResult(
                    $"Cost for {ExampleQuantity:#,0.####} {unit.Abbreviation} of {material.Name}",
                    baseQuantity,
                    material.BaseUnit?.Abbreviation ?? string.Empty,
                    material.CostPerBaseUnit,
                    baseQuantity * material.CostPerBaseUnit);
            },
            this.WhenAnyValue(x => x.ExampleMaterial, x => x.ExampleUnit, x => x.ExampleQuantity,
                (material, unit, quantity) => material is not null && unit is not null && quantity > 0));

        DeletePriceCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync("Delete this price entry? This can't be undone.", async () =>
            {
                await _priceHistoryRepository.DeleteAsync(SelectedPriceRow!.Entry.Id, CancellationToken.None);
                await RefreshSelectedPackagingMaterialAsync();
            }),
            this.WhenAnyValue(x => x.SelectedPriceRow).Select(row => row is not null));

        NewCategoryCommand = ReactiveCommand.Create(() =>
        {
            SelectedCategory = null;
            _editingCategoryId = 0;
            EditingCategoryName = string.Empty;
        });

        SaveCategoryCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var category = new PackagingCategory { Id = _editingCategoryId, Name = EditingCategoryName };

            var validation = _categoryValidator.Validate(category);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingCategoryId == 0)
            {
                await _categoryRepository.AddAsync(category, CancellationToken.None);
            }
            else
            {
                await _categoryRepository.UpdateAsync(category, CancellationToken.None);
            }

            // The whole screen, not just the categories: a renamed category shows in the grid too.
            await LoadCommand.Execute();
            SelectedCategory = null;
        }));

        DeleteCategoryCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete the '{SelectedCategory!.Name}' category? This can't be undone.", async () =>
            {
                await _categoryRepository.DeleteAsync(SelectedCategory!.Id, CancellationToken.None);
                await LoadCommand.Execute();
                SelectedCategory = null;
            }),
            this.WhenAnyValue(x => x.SelectedCategory).Select(category => category is not null));
    }

    private void LoadIntoForm(PackagingMaterial packagingMaterial)
    {
        _editingId = packagingMaterial.Id;
        EditingName = packagingMaterial.Name;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == packagingMaterial.PackagingCategoryId);
        EditingBaseUnit = UnitsOfMeasure.FirstOrDefault(u => u.Id == packagingMaterial.BaseUnitId);
    }

    /// <summary>The Alternative Units rows and the purchase unit choices, from the material's own units.</summary>
    private void RebuildUnitViews(PackagingMaterial? material)
    {
        SelectedUnitRow = null;
        UnitRows.Clear();
        UnitRows.AddRange((material?.Units ?? []).Select(u => new PackagingMaterialUnitRow(u, material!.BaseUnit)));
        CanAddUnit = material is not null && UnitRows.Count < UnitConversionRules.MaxAlternativeUnits;

        PurchaseUnitOptions.Clear();
        PurchaseUnitOptions.AddRange(PackagingUnitOption.For(material));

        UpdateCanChangeBaseUnit();
    }

    private void UpdateCanChangeBaseUnit() =>
        CanChangeBaseUnit = SelectedPackagingMaterial is null || (UnitRows.Count == 0 && PriceHistory.Count == 0);

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
            ? PurchaseUnitOptions.FirstOrDefault(o => o.UnitId == latest.PackagingMaterialUnitId)
            : PurchaseUnitOptions.Skip(1).FirstOrDefault() ?? PurchaseUnitOptions.FirstOrDefault();
        EditingPrice = latest?.Price;
    }

    private async Task LoadPriceHistoryAsync(PackagingMaterial? material)
    {
        PriceHistory.Clear();

        if (material is not null)
        {
            var entries = await _priceHistoryRepository.GetForPackagingMaterialAsync(material.Id, CancellationToken.None);
            PriceHistory.AddRange(entries.Select(e => new PackagingPriceRow(e, material.BaseUnit)));
        }

        LastUpdatedText = PriceHistory.FirstOrDefault()?.Date.ToString("MMM d, yyyy") ?? "—";
        UpdateCanChangeBaseUnit();
        ResetPurchaseSetup();
    }

    /// <summary>
    /// Re-fetches the selected material (its units or CostPerBaseUnit just changed) and pushes it
    /// back into the grid's cache and the selection, which also reloads its details.
    /// </summary>
    private async Task RefreshSelectedPackagingMaterialAsync()
    {
        if (SelectedPackagingMaterial is null)
        {
            return;
        }

        var refreshed = await _packagingMaterialRepository.GetByIdAsync(SelectedPackagingMaterial.Id, CancellationToken.None);
        if (refreshed is not null)
        {
            _packagingMaterialCache.AddOrUpdate(refreshed);
            SelectedPackagingMaterial = refreshed;
        }
    }
}
