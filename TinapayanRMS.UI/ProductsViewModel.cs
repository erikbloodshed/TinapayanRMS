using System.Collections.ObjectModel;
using System.Reactive.Linq;
using DynamicData;
using DynamicData.Binding;
using FluentValidation;
using ReactiveUI;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

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

/// <summary>A row in the Tab Links grid: a link plus the display name of the role it's for (or "Packaging").</summary>
public class TabLinkRow(ProductTabLink link)
{
    public ProductTabLink Link { get; } = link;

    public string TabName => Link.TabKind == ProductTabKind.Packaging ? "Packaging" : Link.SubAssemblyRole?.Name ?? "?";

    public string LinkedToProductName => Link.LinkedProduct?.Name ?? "?";
}

public class ProductsViewModel : ViewModelBase
{
    private readonly IProductRepository _productRepository;
    private readonly IProductSubAssemblyUsageRepository _usageRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingParametersRepository _costingParametersRepository;
    private readonly IProductTabLinkRepository _tabLinkRepository;
    private readonly IProductCloner _productCloner;
    private readonly IValidator<Product> _productValidator;
    private readonly IValidator<CostingParameters> _costingParametersValidator;
    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<Product> _products;

    public ReadOnlyObservableCollection<Product> Products => _products;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    public ObservableCollection<SubAssembly> SubAssemblies { get; } = [];

    public ObservableCollection<SubAssembly> SubAssembliesForSelectedRole { get; } = [];

    public ObservableCollection<PackagingMaterial> PackagingMaterials { get; } = [];

    private readonly List<UnitOfMeasure> _allUnits = [];

    public ObservableCollection<UnitOfMeasure> EntryUnitOptions { get; } = [];

    public IReadOnlyList<PackagingLineAuthoredField> AuthoredFieldOptions { get; } = Enum.GetValues<PackagingLineAuthoredField>();

    public IReadOnlyList<ProductTabKind> TabKindOptions { get; } = Enum.GetValues<ProductTabKind>();

    public ObservableCollection<ProductUsageEdit> UsageEdits { get; } = [];

    public ObservableCollection<PackagingLineEdit> LineEdits { get; } = [];

    public ObservableCollection<TabLinkRow> TabLinks { get; } = [];

    public ObservableCollection<Product> LinkTargetProductOptions { get; } = [];

    /// <summary>
    /// Every role's last-saved effective usage costs, keyed by role id — used so the cost
    /// summary reflects the whole product, not just the currently-selected tab. The
    /// currently-selected role's entry is always superseded by the live UsageEdits list
    /// when recomputing (see RecomputeSummary), so it's never double-counted.
    /// </summary>
    private Dictionary<int, List<decimal>> _effectiveUsageCostsPerPieceByRoleId = [];

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

    private SubAssemblyRole? _editingUsageRole;
    public SubAssemblyRole? EditingUsageRole
    {
        get => _editingUsageRole;
        set => this.RaiseAndSetIfChanged(ref _editingUsageRole, value);
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

    private bool _isSelectedRoleLinked;
    public bool IsSelectedRoleLinked
    {
        get => _isSelectedRoleLinked;
        private set => this.RaiseAndSetIfChanged(ref _isSelectedRoleLinked, value);
    }

    private bool _isPackagingLinked;
    public bool IsPackagingLinked
    {
        get => _isPackagingLinked;
        private set => this.RaiseAndSetIfChanged(ref _isPackagingLinked, value);
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

    private ProductTabKind _editingLinkTabKind;
    public ProductTabKind EditingLinkTabKind
    {
        get => _editingLinkTabKind;
        set => this.RaiseAndSetIfChanged(ref _editingLinkTabKind, value);
    }

    private SubAssemblyRole? _editingLinkRole;
    public SubAssemblyRole? EditingLinkRole
    {
        get => _editingLinkRole;
        set => this.RaiseAndSetIfChanged(ref _editingLinkRole, value);
    }

    private Product? _editingLinkTargetProduct;
    public Product? EditingLinkTargetProduct
    {
        get => _editingLinkTargetProduct;
        set => this.RaiseAndSetIfChanged(ref _editingLinkTargetProduct, value);
    }

    private TabLinkRow? _selectedTabLink;
    public TabLinkRow? SelectedTabLink
    {
        get => _selectedTabLink;
        set => this.RaiseAndSetIfChanged(ref _selectedTabLink, value);
    }

    private string _editingCloneName = string.Empty;
    public string EditingCloneName
    {
        get => _editingCloneName;
        set => this.RaiseAndSetIfChanged(ref _editingCloneName, value);
    }

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
    public ReactiveCommand<RxVoid, RxVoid> LinkTabCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> UnlinkTabCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloneCommand { get; }

    public ProductsViewModel(
        IProductRepository productRepository,
        IProductSubAssemblyUsageRepository usageRepository,
        IPackagingLineRepository packagingLineRepository,
        ICostingParametersRepository costingParametersRepository,
        IProductTabLinkRepository tabLinkRepository,
        IProductCloner productCloner,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyRoleRepository subAssemblyRoleRepository,
        IPackagingMaterialRepository packagingMaterialRepository,
        IUnitOfMeasureRepository unitOfMeasureRepository,
        IValidator<Product> productValidator,
        IValidator<CostingParameters> costingParametersValidator,
        ILogger logger)
        : base(logger)
    {
        _productRepository = productRepository;
        _usageRepository = usageRepository;
        _packagingLineRepository = packagingLineRepository;
        _costingParametersRepository = costingParametersRepository;
        _tabLinkRepository = tabLinkRepository;
        _productCloner = productCloner;
        _productValidator = productValidator;
        _costingParametersValidator = costingParametersValidator;

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
            .Subscribe(product =>
            {
                SelectedUsageEdit = null;
                SelectedLineEdit = null;
                SelectedTabLink = null;
                EditingUsageSubAssembly = null;
                EditingUsageWeightPerPiece = 0m;
                EditingLinePackagingMaterial = null;
                EditingLineAuthoredValue = 0m;
                EditingLaborCost = 0m;
                EditingOverheadCost = 0m;
                EditingMarginPercent = 0m;
                EditingVatPercent = 0m;
                EditingCloneName = product is not null ? $"{product.Name} Copy" : string.Empty;

                RefreshLinkTargetProductOptions();
            });

        this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => Observable.FromAsync(() => LoadProductDetailAsync(product)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.EditingUsageRole)
            .Select(role => Observable.FromAsync(() => LoadUsagesForRoleAsync(role)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.EditingLinePackagingMaterial)
            .Subscribe(material => RefreshEntryUnitOptions(material));

        this.WhenAnyValue(x => x.EditingLaborCost, x => x.EditingOverheadCost, x => x.EditingMarginPercent, x => x.EditingVatPercent)
            .Subscribe(_ => RecomputeSummary());

        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await subAssemblyRoleRepository.GetAllAsync(CancellationToken.None);
            Roles.Clear();
            Roles.AddRange(roles);

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

            RefreshLinkTargetProductOptions();
        });

        NewCommand = ReactiveCommand.Create(() =>
        {
            SelectedProduct = null;
            _editingId = 0;
            EditingName = string.Empty;
            UsageEdits.Clear();
            LineEdits.Clear();
            TabLinks.Clear();
            EditingUsageRole = Roles.FirstOrDefault();
            RecomputeSummary();
        });

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            var product = new Product { Id = _editingId, Name = EditingName };

            var validation = _productValidator.Validate(product);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            if (_editingId == 0)
            {
                await _productRepository.AddAsync(product, CancellationToken.None);
            }
            else
            {
                await _productRepository.UpdateAsync(product, CancellationToken.None);
            }

            await LoadCommand.Execute();
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedProduct!.Name}'? This can't be undone.", async () =>
            {
                await _productRepository.DeleteAsync(SelectedProduct!.Id, CancellationToken.None);
                SelectedProduct = null;
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        AddUsageCommand = ReactiveCommand.Create(
            () =>
            {
                UsageEdits.Add(new ProductUsageEdit(EditingUsageSubAssembly!, EditingUsageWeightPerPiece));
                RecomputeSummary();
            },
            this.WhenAnyValue(
                x => x.EditingUsageSubAssembly, x => x.EditingUsageWeightPerPiece, x => x.IsSelectedRoleLinked,
                (subAssembly, grams, linked) => subAssembly is not null && grams > 0 && !linked));

        RemoveUsageCommand = ReactiveCommand.Create(
            () =>
            {
                UsageEdits.Remove(SelectedUsageEdit!);
                RecomputeSummary();
            },
            this.WhenAnyValue(x => x.SelectedUsageEdit, x => x.IsSelectedRoleLinked, (usage, linked) => usage is not null && !linked));

        SaveUsagesCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var usages = UsageEdits.Select(u => (u.SubAssembly.Id, u.WeightPerPiece)).ToList();
                await _usageRepository.SetUsagesForRoleAsync(SelectedProduct!.Id, EditingUsageRole!.Id, usages, CancellationToken.None);
                await LoadEffectiveUsageCostsAsync(SelectedProduct);
                await LoadUsagesForRoleAsync(EditingUsageRole);
                RecomputeSummary();
            }),
            this.WhenAnyValue(
                x => x.SelectedProduct, x => x.EditingUsageRole, x => x.IsSelectedRoleLinked,
                (product, role, linked) => product is not null && role is not null && !linked));

        AddLineCommand = ReactiveCommand.Create(
            () =>
            {
                LineEdits.Add(new PackagingLineEdit(
                    EditingLinePackagingMaterial!, EditingLineEntryUnit!, EditingLineAuthoredField, EditingLineAuthoredValue));
                RecomputeSummary();
            },
            this.WhenAnyValue(
                x => x.EditingLinePackagingMaterial, x => x.EditingLineEntryUnit, x => x.EditingLineAuthoredValue, x => x.IsPackagingLinked,
                (material, unit, value, linked) => material is not null && unit is not null && value > 0 && !linked));

        RemoveLineCommand = ReactiveCommand.Create(
            () =>
            {
                LineEdits.Remove(SelectedLineEdit!);
                RecomputeSummary();
            },
            this.WhenAnyValue(x => x.SelectedLineEdit, x => x.IsPackagingLinked, (line, linked) => line is not null && !linked));

        SaveLinesCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var lines = LineEdits
                    .Select(l => (l.PackagingMaterial.Id, l.EntryUnit.Id, l.AuthoredField, l.AuthoredValue))
                    .ToList();
                await _packagingLineRepository.SetLinesAsync(SelectedProduct!.Id, lines, CancellationToken.None);
                await LoadProductDetailAsync(SelectedProduct);
            }),
            this.WhenAnyValue(x => x.SelectedProduct, x => x.IsPackagingLinked, (product, linked) => product is not null && !linked));

        SaveCostingCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var costingParameters = new CostingParameters
                {
                    ProductId = SelectedProduct!.Id,
                    LaborCost = EditingLaborCost,
                    OverheadCost = EditingOverheadCost,
                    MarginPercent = EditingMarginPercent,
                    VatPercent = EditingVatPercent,
                };

                var validation = _costingParametersValidator.Validate(costingParameters);
                if (!validation.IsValid)
                {
                    ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                    return;
                }

                await _costingParametersRepository.SetAsync(
                    SelectedProduct!.Id, EditingLaborCost, EditingOverheadCost, EditingMarginPercent, EditingVatPercent, CancellationToken.None);
                RecomputeSummary();
            }),
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        LinkTabCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var roleId = EditingLinkTabKind == ProductTabKind.SubAssemblyRole ? EditingLinkRole!.Id : (int?)null;
                await _tabLinkRepository.SetLinkAsync(SelectedProduct!.Id, EditingLinkTabKind, roleId, EditingLinkTargetProduct!.Id, CancellationToken.None);
                await LoadProductDetailAsync(SelectedProduct);
            }),
            this.WhenAnyValue(
                x => x.SelectedProduct, x => x.EditingLinkTabKind, x => x.EditingLinkRole, x => x.EditingLinkTargetProduct,
                (product, tabKind, role, target) => product is not null && target is not null && (tabKind == ProductTabKind.Packaging || role is not null)));

        UnlinkTabCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var link = SelectedTabLink!.Link;
                await _tabLinkRepository.RemoveLinkAsync(SelectedProduct!.Id, link.TabKind, link.SubAssemblyRoleId, CancellationToken.None);
                await LoadProductDetailAsync(SelectedProduct);
            }),
            this.WhenAnyValue(x => x.SelectedTabLink).Select(link => link is not null));

        CloneCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var newId = await _productCloner.CloneAsync(SelectedProduct!.Id, EditingCloneName, CancellationToken.None);
                await LoadCommand.Execute();
                SelectedProduct = Products.FirstOrDefault(p => p.Id == newId);
            }),
            this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingCloneName, (product, name) => product is not null && !string.IsNullOrWhiteSpace(name)));
    }

    private void LoadIntoForm(Product product)
    {
        _editingId = product.Id;
        EditingName = product.Name;
    }

    private void RefreshLinkedFlags()
    {
        IsSelectedRoleLinked = EditingUsageRole is not null
            && TabLinks.Any(l => l.Link.TabKind == ProductTabKind.SubAssemblyRole && l.Link.SubAssemblyRoleId == EditingUsageRole.Id);
        IsPackagingLinked = TabLinks.Any(l => l.Link.TabKind == ProductTabKind.Packaging);
    }

    private void RefreshLinkTargetProductOptions()
    {
        LinkTargetProductOptions.Clear();
        LinkTargetProductOptions.AddRange(SelectedProduct is null ? Products : Products.Where(p => p.Id != SelectedProduct.Id));
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
        LineEdits.Clear();
        TabLinks.Clear();

        if (product is null)
        {
            _effectiveUsageCostsPerPieceByRoleId = [];
            RecomputeSummary();
            return;
        }

        var links = await _tabLinkRepository.GetForProductAsync(product.Id, CancellationToken.None);
        TabLinks.AddRange(links.Select(l => new TabLinkRow(l)));
        RefreshLinkedFlags();

        var lines = await _packagingLineRepository.GetEffectiveForProductAsync(product.Id, CancellationToken.None);
        foreach (var line in lines)
        {
            LineEdits.Add(new PackagingLineEdit(line.PackagingMaterial!, line.EntryUnit!, line.AuthoredField, line.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? line.QtyPerPiece : line.PiecesPerPackagingUnit));
        }

        var costingParameters = await _costingParametersRepository.GetForProductAsync(product.Id, CancellationToken.None);
        EditingLaborCost = costingParameters?.LaborCost ?? 0m;
        EditingOverheadCost = costingParameters?.OverheadCost ?? 0m;
        EditingMarginPercent = costingParameters?.MarginPercent ?? 0m;
        EditingVatPercent = costingParameters?.VatPercent ?? 0m;

        await LoadEffectiveUsageCostsAsync(product);

        EditingUsageRole = Roles.FirstOrDefault();
        // If the role didn't actually change (already the first role from a prior
        // selection), the WhenAnyValue subscription above won't re-fire on its own.
        await LoadUsagesForRoleAsync(EditingUsageRole);

        RecomputeSummary();
    }

    /// <summary>Loads every role's last-saved effective usage costs (not just the selected one) so the cost summary always reflects the whole product.</summary>
    private async Task LoadEffectiveUsageCostsAsync(Product? product)
    {
        if (product is null)
        {
            _effectiveUsageCostsPerPieceByRoleId = [];
            return;
        }

        var allUsages = await _usageRepository.GetEffectiveForProductAsync(product.Id, CancellationToken.None);
        _effectiveUsageCostsPerPieceByRoleId = allUsages
            .GroupBy(u => u.SubAssembly!.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(u => ProductRules.CalculateUsageCostPerPiece(u.SubAssembly!.TotalCost, u.WeightPerPiece, u.SubAssembly.TotalWeight)).ToList());
    }

    private async Task LoadUsagesForRoleAsync(SubAssemblyRole? role)
    {
        UsageEdits.Clear();
        SelectedUsageEdit = null;
        EditingUsageSubAssembly = null;

        SubAssembliesForSelectedRole.Clear();
        if (role is not null)
        {
            SubAssembliesForSelectedRole.AddRange(SubAssemblies.Where(s => s.RoleId == role.Id));
        }

        RefreshLinkedFlags();

        if (SelectedProduct is null || role is null)
        {
            return;
        }

        var usages = await _usageRepository.GetEffectiveForRoleAsync(SelectedProduct.Id, role.Id, CancellationToken.None);
        foreach (var usage in usages)
        {
            UsageEdits.Add(new ProductUsageEdit(usage.SubAssembly!, usage.WeightPerPiece));
        }
    }

    /// <summary>
    /// Sums every role's usage cost plus every packaging line's cost. The currently
    /// selected role always contributes its *live* UsageEdits (including unsaved Add/
    /// Remove changes), never its last-saved entry in
    /// _effectiveUsageCostsPerPieceByRoleId — otherwise an unsaved edit to the selected
    /// role would double-count (once from the cache, once from UsageEdits) or go
    /// unreflected until Save.
    /// </summary>
    private void RecomputeSummary()
    {
        var usageCosts = new List<decimal>();
        foreach (var (roleId, costs) in _effectiveUsageCostsPerPieceByRoleId)
        {
            if (EditingUsageRole is not null && roleId == EditingUsageRole.Id)
            {
                continue;
            }

            usageCosts.AddRange(costs);
        }

        if (EditingUsageRole is not null)
        {
            usageCosts.AddRange(UsageEdits.Select(u => u.CostPerPiece));
        }

        var packagingCosts = LineEdits.Select(l => l.CostPerPiece).ToList();
        MaterialCostPerPiece = ProductRules.CalculateMaterialCostPerPiece(usageCosts, packagingCosts);

        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            MaterialCostPerPiece, EditingLaborCost, EditingOverheadCost, EditingMarginPercent, EditingVatPercent);
        BaseCost = baseCost;
        PreVatPrice = preVatPrice;
        SellingPrice = sellingPrice;
    }
}
