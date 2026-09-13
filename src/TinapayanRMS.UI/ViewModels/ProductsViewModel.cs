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

/// <summary>An in-memory (not yet saved) sub-assembly usage, shown in the usages editor grid.</summary>
public class ProductUsageEdit(SubAssembly subAssembly, decimal weightPerPiece)
{
    public SubAssembly SubAssembly { get; } = subAssembly;

    public decimal WeightPerPiece { get; } = weightPerPiece;

    /// <summary>Informational only — expected to differ across a product's usages.</summary>
    public decimal Yield => ProductRules.CalculateYield(SubAssembly.TotalWeight, WeightPerPiece);

    public decimal CostPerPiece => ProductRules.CalculateUsageCostPerPiece(SubAssembly.TotalCost, WeightPerPiece, SubAssembly.TotalWeight);
}

/// <summary>An in-memory (not yet saved) packaging line, shown in the packaging editor grid: quantities in one of the material's units.</summary>
public class PackagingLineEdit(PackagingMaterial packagingMaterial, PackagingUnitOption unit, PackagingLineAuthoredField authoredField, decimal authoredValue)
{
    public PackagingMaterial PackagingMaterial { get; } = packagingMaterial;

    /// <summary>The unit QtyPerPiece and PiecesPerPackagingUnit are in: the material's base unit or one of its alternative units.</summary>
    public PackagingUnitOption Unit { get; } = unit;

    public PackagingLineAuthoredField AuthoredField { get; } = authoredField;

    public decimal AuthoredValue { get; } = authoredValue;

    private (decimal QtyPerPiece, decimal PiecesPerPackagingUnit) Reciprocal =>
        ProductRules.ReconcileReciprocal(AuthoredField, AuthoredValue);

    public decimal QtyPerPiece => Reciprocal.QtyPerPiece;

    public decimal PiecesPerPackagingUnit => Reciprocal.PiecesPerPackagingUnit;

    public decimal CostPerPiece => ProductRules.CalculatePackagingLineCostPerPiece(
        PackagingMaterial.CostPerBaseUnit, QtyPerPiece, Unit.Unit?.BaseUnitsPerUnit);
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

    /// <summary>The picked material's units, for the line editor's unit picker.</summary>
    public ObservableCollection<PackagingUnitOption> EntryUnitOptions { get; } = [];

    private PackagingUnitOption? _editingLineUnit;
    public PackagingUnitOption? EditingLineUnit
    {
        get => _editingLineUnit;
        set => this.RaiseAndSetIfChanged(ref _editingLineUnit, value);
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

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which
        // only re-points a selection at its refreshed copy; the form, detail and unsaved rows must
        // stay as-is.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is not null && !IsRefreshing)
            .Subscribe(product => LoadIntoForm(product!));

        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(_ => !IsRefreshing)
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
            .Where(_ => !IsRefreshing)
            .Select(product => Observable.FromAsync(() => LoadProductDetailAsync(product)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.EditingLinePackagingMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material => ReplaceEntryUnitOptions(material, keepUnitId: null));

        this.WhenAnyValue(x => x.EditingUsageRole)
            .Where(_ => !IsRefreshing)
            .Select(role => Observable.FromAsync(() => LoadUsagesForRoleAsync(role)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.EditingLaborCost, x => x.EditingOverheadCost, x => x.EditingMarginPercent, x => x.EditingVatPercent)
            .Subscribe(_ => RecomputeSummary());

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, sub-tab pickers and unsaved usage
        // and packaging rows while picking up what changed elsewhere, such as a repriced ingredient
        // flowing into a sub-assembly's cost.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await subAssemblyRoleRepository.GetAllAsync(CancellationToken.None);
            var subAssemblies = await subAssemblyRepository.GetAllAsync(CancellationToken.None);
            var packagingMaterials = await packagingMaterialRepository.GetAllAsync(CancellationToken.None);
            var products = await _productRepository.GetAllAsync(CancellationToken.None);

            // The selected product's saved usage costs and tab links can change on other tabs too
            // (a repriced ingredient, a renamed role), so they're fetched here as well.
            var detailProductId = SelectedProduct?.Id;
            IReadOnlyList<ProductSubAssemblyUsage> effectiveUsages = detailProductId is { } usagesProductId
                ? await _usageRepository.GetEffectiveForProductAsync(usagesProductId, CancellationToken.None)
                : [];
            IReadOnlyList<ProductTabLink> links = detailProductId is { } linksProductId
                ? await _tabLinkRepository.GetForProductAsync(linksProductId, CancellationToken.None)
                : [];

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedProduct?.Id;
            var usageRoleId = EditingUsageRole?.Id;
            var usageSubAssemblyId = EditingUsageSubAssembly?.Id;
            var linePackagingMaterialId = EditingLinePackagingMaterial?.Id;
            var lineUnitId = EditingLineUnit?.UnitId;
            var linkRoleId = EditingLinkRole?.Id;
            var linkTargetId = EditingLinkTargetProduct?.Id;

            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                SubAssemblies.MergeFrom([.. subAssemblies.OrderBy(s => s.Name)], s => s.Id);
                PackagingMaterials.MergeFrom([.. packagingMaterials.OrderBy(m => m.Name)], m => m.Id);
                _productCache.MergeFrom(products);

                SelectedProduct = Products.FirstOrDefault(p => p.Id == selectedId);

                EditingUsageRole = Roles.FirstOrDefault(r => r.Id == usageRoleId);
                SubAssembliesForSelectedRole.MergeFrom([.. SubAssemblies.Where(s => s.RoleId == EditingUsageRole?.Id)], s => s.Id);
                EditingUsageSubAssembly = SubAssembliesForSelectedRole.FirstOrDefault(s => s.Id == usageSubAssemblyId);

                var previousLineMaterial = EditingLinePackagingMaterial;
                EditingLinePackagingMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == linePackagingMaterialId);
                if (!ReferenceEquals(previousLineMaterial, EditingLinePackagingMaterial))
                {
                    ReplaceEntryUnitOptions(EditingLinePackagingMaterial, lineUnitId);
                }

                EditingLinkRole = Roles.FirstOrDefault(r => r.Id == linkRoleId);
                RefreshLinkTargetProductOptions();
                EditingLinkTargetProduct = LinkTargetProductOptions.FirstOrDefault(p => p.Id == linkTargetId);

                RefreshUnsavedRows();

                if (SelectedProduct is not null && SelectedProduct.Id == detailProductId)
                {
                    _effectiveUsageCostsPerPieceByRoleId = UsageCostsPerPieceByRole(effectiveUsages);
                    ReplaceTabLinks(links);
                }

                RecomputeSummary();
            });
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
                LineEdits.Add(new PackagingLineEdit(EditingLinePackagingMaterial!, EditingLineUnit!, EditingLineAuthoredField, EditingLineAuthoredValue));
                RecomputeSummary();
            },
            this.WhenAnyValue(
                x => x.EditingLinePackagingMaterial, x => x.EditingLineUnit, x => x.EditingLineAuthoredValue, x => x.IsPackagingLinked,
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
                    .Select(l => (l.PackagingMaterial.Id, l.Unit.UnitId, l.AuthoredField, l.AuthoredValue))
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
        LinkTargetProductOptions.MergeFrom(
            [.. SelectedProduct is null ? Products : Products.Where(p => p.Id != SelectedProduct.Id)],
            p => p.Id);
    }

    /// <summary>Fills the line editor's unit picker with <paramref name="material"/>'s units and picks the one with <paramref name="keepUnitId"/>, else its base unit.</summary>
    private void ReplaceEntryUnitOptions(PackagingMaterial? material, int? keepUnitId)
    {
        EditingLineUnit = null;
        EntryUnitOptions.Clear();
        EntryUnitOptions.AddRange(PackagingUnitOption.For(material));
        EditingLineUnit = EntryUnitOptions.FirstOrDefault(o => o.UnitId == keepUnitId) ?? EntryUnitOptions.FirstOrDefault();
    }

    /// <summary>
    /// Points the unsaved usage and packaging rows at the refreshed sub-assemblies and materials,
    /// keeping each row's own numbers, so their costs reflect prices changed on other tabs.
    /// A row whose sub-assembly or material didn't change is left alone.
    /// </summary>
    private void RefreshUnsavedRows()
    {
        for (var i = 0; i < UsageEdits.Count; i++)
        {
            var edit = UsageEdits[i];
            var subAssembly = SubAssemblies.FirstOrDefault(s => s.Id == edit.SubAssembly.Id);
            if (subAssembly is null || CollectionMerge.HasSameValues(subAssembly, edit.SubAssembly))
            {
                continue;
            }

            var wasSelected = ReferenceEquals(SelectedUsageEdit, edit);
            UsageEdits[i] = new ProductUsageEdit(subAssembly, edit.WeightPerPiece);
            if (wasSelected)
            {
                SelectedUsageEdit = UsageEdits[i];
            }
        }

        for (var i = 0; i < LineEdits.Count; i++)
        {
            var edit = LineEdits[i];
            var material = PackagingMaterials.FirstOrDefault(m => m.Id == edit.PackagingMaterial.Id);
            if (material is null || CollectionMerge.HasSameValues(material, edit.PackagingMaterial))
            {
                continue;
            }

            // The same unit on the refreshed material, or its base unit if that unit is gone.
            var options = PackagingUnitOption.For(material);
            var unit = options.FirstOrDefault(o => o.UnitId == edit.Unit.UnitId) ?? options[0];

            var wasSelected = ReferenceEquals(SelectedLineEdit, edit);
            LineEdits[i] = new PackagingLineEdit(material, unit, edit.AuthoredField, edit.AuthoredValue);
            if (wasSelected)
            {
                SelectedLineEdit = LineEdits[i];
            }
        }
    }

    /// <summary>Swaps in a fresh copy of the selected product's tab links (they show role and product names), keeping the selected link.</summary>
    private void ReplaceTabLinks(IReadOnlyList<ProductTabLink> links)
    {
        var selectedKind = SelectedTabLink?.Link.TabKind;
        var selectedRoleId = SelectedTabLink?.Link.SubAssemblyRoleId;

        TabLinks.Clear();
        TabLinks.AddRange(links.Select(l => new TabLinkRow(l)));
        SelectedTabLink = selectedKind is null
            ? null
            : TabLinks.FirstOrDefault(r => r.Link.TabKind == selectedKind && r.Link.SubAssemblyRoleId == selectedRoleId);

        RefreshLinkedFlags();
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
            LineEdits.Add(new PackagingLineEdit(
                line.PackagingMaterial!,
                new PackagingUnitOption(line.PackagingMaterialUnit, line.PackagingMaterial!.BaseUnit),
                line.AuthoredField,
                line.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? line.QtyPerPiece : line.PiecesPerPackagingUnit));
        }

        var costingParameters = await _costingParametersRepository.GetForProductAsync(product.Id, CancellationToken.None);
        EditingLaborCost = costingParameters?.LaborCost ?? 0m;
        EditingOverheadCost = costingParameters?.OverheadCost ?? 0m;
        EditingMarginPercent = costingParameters?.MarginPercent ?? 0m;
        EditingVatPercent = costingParameters?.VatPercent ?? 0m;

        await LoadEffectiveUsageCostsAsync(product);

        var firstRole = Roles.FirstOrDefault();
        if (ReferenceEquals(EditingUsageRole, firstRole))
        {
            // The role didn't change, so the WhenAnyValue subscription above won't reload its
            // usages for this product on its own.
            await LoadUsagesForRoleAsync(firstRole);
        }
        else
        {
            // The WhenAnyValue subscription above loads its usages. Loading them here too ran two
            // loads side by side, and every usage was listed (and costed) twice.
            EditingUsageRole = firstRole;
        }

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
        _effectiveUsageCostsPerPieceByRoleId = UsageCostsPerPieceByRole(allUsages);
    }

    private static Dictionary<int, List<decimal>> UsageCostsPerPieceByRole(IEnumerable<ProductSubAssemblyUsage> usages) =>
        usages
            .GroupBy(u => u.SubAssembly!.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(u => ProductRules.CalculateUsageCostPerPiece(u.SubAssembly!.TotalCost, u.WeightPerPiece, u.SubAssembly.TotalWeight)).ToList());

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

        // When a role change triggered this load, nothing else recomputes the summary afterwards.
        RecomputeSummary();
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
