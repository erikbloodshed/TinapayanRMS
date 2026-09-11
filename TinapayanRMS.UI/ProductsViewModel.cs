using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using ReactiveUI;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI;

/// <summary>An in-memory (not yet saved) sub-assembly usage, shown in the usages editor grid.</summary>
public class ProductUsageEdit(SubAssembly subAssembly, decimal weightPerPiece)
{
    public SubAssembly SubAssembly { get; } = subAssembly;

    public decimal WeightPerPiece { get; } = weightPerPiece;

    /// <summary>Informational only — expected to differ across a product's usages.</summary>
    public decimal Yield => ProductRules.CalculateYield(SubAssembly.TotalWeight, WeightPerPiece);

    public decimal CostPerPiece => ProductRules.CalculateUsageCostPerPiece(SubAssembly.TotalCost, WeightPerPiece, SubAssembly.TotalWeight);
}

/// <summary>An in-memory (not yet saved) packaging line, shown in the packaging editor grid.</summary>
public class PackagingLineEdit(PackagingMaterial packagingMaterial, UnitOfMeasure entryUnit, PackagingLineAuthoredField authoredField, decimal authoredValue)
{
    public PackagingMaterial PackagingMaterial { get; } = packagingMaterial;

    public UnitOfMeasure EntryUnit { get; } = entryUnit;

    public PackagingLineAuthoredField AuthoredField { get; } = authoredField;

    public decimal AuthoredValue { get; } = authoredValue;

    private (decimal QtyPerPiece, decimal PiecesPerPackagingUnit) Reciprocal =>
        ProductRules.ReconcileReciprocal(AuthoredField, AuthoredValue);

    public decimal QtyPerPiece => Reciprocal.QtyPerPiece;

    public decimal PiecesPerPackagingUnit => Reciprocal.PiecesPerPackagingUnit;

    public decimal CostPerPiece => ProductRules.CalculatePackagingLineCostPerPiece(
        PackagingMaterial.CurrentCost, PackagingMaterial.PurchaseUnitQty, QtyPerPiece, EntryUnit.Id, PackagingMaterial.StandardUnitId);
}

public class ProductsViewModel : ReactiveObject
{
    private readonly IProductRepository _productRepository;
    private readonly IProductSubAssemblyUsageRepository _usageRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingParametersRepository _costingParametersRepository;
    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<Product> _products;

    public ReadOnlyObservableCollection<Product> Products => _products;

    public ObservableCollection<SubAssembly> SubAssemblies { get; } = [];

    public ObservableCollection<PackagingMaterial> PackagingMaterials { get; } = [];

    private readonly List<UnitOfMeasure> _allUnits = [];

    public ObservableCollection<UnitOfMeasure> EntryUnitOptions { get; } = [];

    public IReadOnlyList<PackagingLineAuthoredField> AuthoredFieldOptions { get; } = Enum.GetValues<PackagingLineAuthoredField>();

    public ObservableCollection<ProductUsageEdit> UsageEdits { get; } = [];

    public ObservableCollection<PackagingLineEdit> LineEdits { get; } = [];

    private Product? _selectedProduct;
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set => this.RaiseAndSetIfChanged(ref _selectedProduct, value);
    }

    private int _editingId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private SubAssembly? _editingUsageSubAssembly;
    public SubAssembly? EditingUsageSubAssembly
    {
        get => _editingUsageSubAssembly;
        set => this.RaiseAndSetIfChanged(ref _editingUsageSubAssembly, value);
    }

    private decimal _editingUsageWeightPerPiece;
    public decimal EditingUsageWeightPerPiece
    {
        get => _editingUsageWeightPerPiece;
        set => this.RaiseAndSetIfChanged(ref _editingUsageWeightPerPiece, value);
    }

    private ProductUsageEdit? _selectedUsageEdit;
    public ProductUsageEdit? SelectedUsageEdit
    {
        get => _selectedUsageEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedUsageEdit, value);
    }

    private PackagingMaterial? _editingLinePackagingMaterial;
    public PackagingMaterial? EditingLinePackagingMaterial
    {
        get => _editingLinePackagingMaterial;
        set => this.RaiseAndSetIfChanged(ref _editingLinePackagingMaterial, value);
    }

    private UnitOfMeasure? _editingLineEntryUnit;
    public UnitOfMeasure? EditingLineEntryUnit
    {
        get => _editingLineEntryUnit;
        set => this.RaiseAndSetIfChanged(ref _editingLineEntryUnit, value);
    }

    private PackagingLineAuthoredField _editingLineAuthoredField;
    public PackagingLineAuthoredField EditingLineAuthoredField
    {
        get => _editingLineAuthoredField;
        set => this.RaiseAndSetIfChanged(ref _editingLineAuthoredField, value);
    }

    private decimal _editingLineAuthoredValue;
    public decimal EditingLineAuthoredValue
    {
        get => _editingLineAuthoredValue;
        set => this.RaiseAndSetIfChanged(ref _editingLineAuthoredValue, value);
    }

    private PackagingLineEdit? _selectedLineEdit;
    public PackagingLineEdit? SelectedLineEdit
    {
        get => _selectedLineEdit;
        set => this.RaiseAndSetIfChanged(ref _selectedLineEdit, value);
    }

    private decimal _editingLaborCost;
    public decimal EditingLaborCost
    {
        get => _editingLaborCost;
        set => this.RaiseAndSetIfChanged(ref _editingLaborCost, value);
    }

    private decimal _editingOverheadCost;
    public decimal EditingOverheadCost
    {
        get => _editingOverheadCost;
        set => this.RaiseAndSetIfChanged(ref _editingOverheadCost, value);
    }

    private decimal _editingMarginPercent;
    public decimal EditingMarginPercent
    {
        get => _editingMarginPercent;
        set => this.RaiseAndSetIfChanged(ref _editingMarginPercent, value);
    }

    private decimal _editingVatPercent;
    public decimal EditingVatPercent
    {
        get => _editingVatPercent;
        set => this.RaiseAndSetIfChanged(ref _editingVatPercent, value);
    }

    private decimal _materialCostPerPiece;
    public decimal MaterialCostPerPiece
    {
        get => _materialCostPerPiece;
        private set => this.RaiseAndSetIfChanged(ref _materialCostPerPiece, value);
    }

    private decimal _baseCost;
    public decimal BaseCost
    {
        get => _baseCost;
        private set => this.RaiseAndSetIfChanged(ref _baseCost, value);
    }

    private decimal _preVatPrice;
    public decimal PreVatPrice
    {
        get => _preVatPrice;
        private set => this.RaiseAndSetIfChanged(ref _preVatPrice, value);
    }

    private decimal _sellingPrice;
    public decimal SellingPrice
    {
        get => _sellingPrice;
        private set => this.RaiseAndSetIfChanged(ref _sellingPrice, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _showProductDetail;
    public bool ShowProductDetail => _showProductDetail.Value;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveUsagesCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveLinesCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCostingCommand { get; }

    public ProductsViewModel(
        IProductRepository productRepository,
        IProductSubAssemblyUsageRepository usageRepository,
        IPackagingLineRepository packagingLineRepository,
        ICostingParametersRepository costingParametersRepository,
        ISubAssemblyRepository subAssemblyRepository,
        IPackagingMaterialRepository packagingMaterialRepository,
        ILookupRepository<UnitOfMeasure> unitOfMeasureRepository)
    {
        _productRepository = productRepository;
        _usageRepository = usageRepository;
        _packagingLineRepository = packagingLineRepository;
        _costingParametersRepository = costingParametersRepository;

        _productCache.Connect()
            .SortAndBind(out _products, SortExpressionComparer<Product>.Ascending(p => p.Name))
            .Subscribe();

        _showProductDetail = this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => product is not null)
            .ToProperty(this, x => x.ShowProductDetail);

        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is not null)
            .Subscribe(product => LoadIntoForm(product!));

        this.WhenAnyValue(x => x.SelectedProduct)
            .Subscribe(_ =>
            {
                SelectedUsageEdit = null;
                SelectedLineEdit = null;
                EditingUsageSubAssembly = null;
                EditingUsageWeightPerPiece = 0m;
                EditingLinePackagingMaterial = null;
                EditingLineAuthoredValue = 0m;
                EditingLaborCost = 0m;
                EditingOverheadCost = 0m;
                EditingMarginPercent = 0m;
                EditingVatPercent = 0m;
            });

        this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => Observable.FromAsync(() => LoadProductDetailAsync(product)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.EditingLinePackagingMaterial)
            .Subscribe(material => RefreshEntryUnitOptions(material));

        this.WhenAnyValue(x => x.EditingLaborCost, x => x.EditingOverheadCost, x => x.EditingMarginPercent, x => x.EditingVatPercent)
            .Subscribe(_ => RecomputeSummary());

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var subAssemblies = await subAssemblyRepository.GetAllAsync(CancellationToken.None);
            SubAssemblies.Clear();
            SubAssemblies.AddRange(subAssemblies.OrderBy(s => s.Name));

            var packagingMaterials = await packagingMaterialRepository.GetAllAsync(CancellationToken.None);
            PackagingMaterials.Clear();
            PackagingMaterials.AddRange(packagingMaterials.OrderBy(m => m.Name));

            var units = await unitOfMeasureRepository.GetAllAsync(CancellationToken.None);
            _allUnits.Clear();
            _allUnits.AddRange(units);

            var products = await _productRepository.GetAllAsync(CancellationToken.None);
            _productCache.Edit(cache =>
            {
                cache.Clear();
                cache.AddOrUpdate(products);
            });
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedProduct = null;
            _editingId = 0;
            EditingName = string.Empty;
            UsageEdits.Clear();
            LineEdits.Clear();
            RecomputeSummary();
        });

        SaveCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var product = new Product { Id = _editingId, Name = EditingName };

            if (_editingId == 0)
            {
                await _productRepository.AddAsync(product, CancellationToken.None);
            }
            else
            {
                await _productRepository.UpdateAsync(product, CancellationToken.None);
            }

            await LoadCommand.Execute();
        });

        DeleteCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _productRepository.DeleteAsync(SelectedProduct!.Id, CancellationToken.None);
                SelectedProduct = null;
                await LoadCommand.Execute();
            },
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        AddUsageCommand = ReactiveCommand.Create(
            () =>
            {
                UsageEdits.Add(new ProductUsageEdit(EditingUsageSubAssembly!, EditingUsageWeightPerPiece));
                RecomputeSummary();
            },
            this.WhenAnyValue(x => x.EditingUsageSubAssembly, x => x.EditingUsageWeightPerPiece, (subAssembly, grams) => subAssembly is not null && grams > 0));

        RemoveUsageCommand = ReactiveCommand.Create(
            () =>
            {
                UsageEdits.Remove(SelectedUsageEdit!);
                RecomputeSummary();
            },
            this.WhenAnyValue(x => x.SelectedUsageEdit).Select(usage => usage is not null));

        SaveUsagesCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                var usages = UsageEdits.Select(u => (u.SubAssembly.Id, u.WeightPerPiece)).ToList();
                await _usageRepository.SetUsagesAsync(SelectedProduct!.Id, usages, CancellationToken.None);
                await LoadProductDetailAsync(SelectedProduct);
            },
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        AddLineCommand = ReactiveCommand.Create(
            () =>
            {
                LineEdits.Add(new PackagingLineEdit(
                    EditingLinePackagingMaterial!, EditingLineEntryUnit!, EditingLineAuthoredField, EditingLineAuthoredValue));
                RecomputeSummary();
            },
            this.WhenAnyValue(
                x => x.EditingLinePackagingMaterial, x => x.EditingLineEntryUnit, x => x.EditingLineAuthoredValue,
                (material, unit, value) => material is not null && unit is not null && value > 0));

        RemoveLineCommand = ReactiveCommand.Create(
            () =>
            {
                LineEdits.Remove(SelectedLineEdit!);
                RecomputeSummary();
            },
            this.WhenAnyValue(x => x.SelectedLineEdit).Select(line => line is not null));

        SaveLinesCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                var lines = LineEdits
                    .Select(l => (l.PackagingMaterial.Id, l.EntryUnit.Id, l.AuthoredField, l.AuthoredValue))
                    .ToList();
                await _packagingLineRepository.SetLinesAsync(SelectedProduct!.Id, lines, CancellationToken.None);
                await LoadProductDetailAsync(SelectedProduct);
            },
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        SaveCostingCommand = ReactiveCommand.CreateFromTask(
            async () =>
            {
                await _costingParametersRepository.SetAsync(
                    SelectedProduct!.Id, EditingLaborCost, EditingOverheadCost, EditingMarginPercent, EditingVatPercent, CancellationToken.None);
                RecomputeSummary();
            },
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));
    }

    private void LoadIntoForm(Product product)
    {
        _editingId = product.Id;
        EditingName = product.Name;
    }

    /// <summary>
    /// A packaging line's entry unit is the material's own standard unit, or — only when
    /// that standard unit is itself a length unit — any of the four length units
    /// (ProductRules.EnsureValidEntryUnit). Defaults the picker to the material's own unit.
    /// </summary>
    private void RefreshEntryUnitOptions(PackagingMaterial? material)
    {
        EntryUnitOptions.Clear();
        EditingLineEntryUnit = null;

        if (material is null)
        {
            return;
        }

        if (LengthConversion.IsLengthUnit(material.StandardUnitId))
        {
            foreach (var unit in _allUnits
                         .Where(u => LengthConversion.IsLengthUnit(u.Id))
                         .OrderBy(u => u.Name))
            {
                EntryUnitOptions.Add(unit);
            }
        }
        else
        {
            var standardUnit = _allUnits.FirstOrDefault(u => u.Id == material.StandardUnitId);
            if (standardUnit is not null)
            {
                EntryUnitOptions.Add(standardUnit);
            }
        }

        EditingLineEntryUnit = EntryUnitOptions.FirstOrDefault(u => u.Id == material.StandardUnitId) ?? EntryUnitOptions.FirstOrDefault();
    }

    private async Task LoadProductDetailAsync(Product? product)
    {
        UsageEdits.Clear();
        LineEdits.Clear();

        if (product is null)
        {
            RecomputeSummary();
            return;
        }

        var usages = await _usageRepository.GetForProductAsync(product.Id, CancellationToken.None);
        foreach (var usage in usages)
        {
            UsageEdits.Add(new ProductUsageEdit(usage.SubAssembly!, usage.WeightPerPiece));
        }

        var lines = await _packagingLineRepository.GetForProductAsync(product.Id, CancellationToken.None);
        foreach (var line in lines)
        {
            LineEdits.Add(new PackagingLineEdit(line.PackagingMaterial!, line.EntryUnit!, line.AuthoredField, line.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? line.QtyPerPiece : line.PiecesPerPackagingUnit));
        }

        var costingParameters = await _costingParametersRepository.GetForProductAsync(product.Id, CancellationToken.None);
        EditingLaborCost = costingParameters?.LaborCost ?? 0m;
        EditingOverheadCost = costingParameters?.OverheadCost ?? 0m;
        EditingMarginPercent = costingParameters?.MarginPercent ?? 0m;
        EditingVatPercent = costingParameters?.VatPercent ?? 0m;

        RecomputeSummary();
    }

    private void RecomputeSummary()
    {
        var usageCosts = UsageEdits.Select(u => u.CostPerPiece).ToList();
        var packagingCosts = LineEdits.Select(l => l.CostPerPiece).ToList();
        MaterialCostPerPiece = ProductRules.CalculateMaterialCostPerPiece(usageCosts, packagingCosts);

        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            MaterialCostPerPiece, EditingLaborCost, EditingOverheadCost, EditingMarginPercent, EditingVatPercent);
        BaseCost = baseCost;
        PreVatPrice = preVatPrice;
        SellingPrice = sellingPrice;
    }
}
