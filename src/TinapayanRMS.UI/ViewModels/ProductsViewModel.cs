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
/// One sub-assembly in the product's recipe, saved or not: a section of the Recipe Structure, with its
/// ingredient lines scaled to one piece, and a box in the Unit Conversion Overview (a batch in pieces).
/// A usage its role's tab reads through a Live Template link carries that link, and is read-only.
/// </summary>
public sealed class ProductUsageEdit
{
    /// <summary>Sections show in role order (SubAssemblyRole.SortOrder), then by name.</summary>
    public static IComparer<ProductUsageEdit> RecipeOrder { get; } = Comparer<ProductUsageEdit>.Create((a, b) =>
    {
        var byRole = (a.SubAssembly.Role?.SortOrder ?? int.MaxValue).CompareTo(b.SubAssembly.Role?.SortOrder ?? int.MaxValue);
        if (byRole == 0)
        {
            byRole = string.Compare(a.RoleName, b.RoleName, StringComparison.CurrentCultureIgnoreCase);
        }

        return byRole != 0 ? byRole : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    });

    public ProductUsageEdit(SubAssembly subAssembly, decimal weightPerPiece, IReadOnlyList<SubAssemblyLine> lines, ProductTabLink? link)
    {
        SubAssembly = subAssembly;
        WeightPerPiece = weightPerPiece;
        Lines = lines;
        Link = link;

        // A sub-assembly with no lines yet weighs nothing, so there's no batch to share out per piece.
        if (subAssembly.TotalWeight > 0)
        {
            Yield = ProductRules.CalculateYield(subAssembly.TotalWeight, weightPerPiece);
            CostPerPiece = ProductRules.CalculateUsageCostPerPiece(subAssembly.TotalCost, weightPerPiece, subAssembly.TotalWeight);
            IngredientRows = [.. lines.Select(l => new RecipeIngredientRow(l, weightPerPiece, subAssembly.TotalWeight, CostPerPiece))];
        }
        else
        {
            IngredientRows = [];
        }
    }

    public SubAssembly SubAssembly { get; }

    public decimal WeightPerPiece { get; }

    /// <summary>The sub-assembly's lines as last loaded, which IngredientRows scales to one piece.</summary>
    public IReadOnlyList<SubAssemblyLine> Lines { get; }

    /// <summary>The Live Template link its role's tab reads through, or null for one of the product's own usages.</summary>
    public ProductTabLink? Link { get; }

    public bool IsLinked => Link is not null;

    public string LinkedToText => $"Linked to {Link?.LinkedProduct?.Name}";

    public string Name => SubAssembly.Name;

    public string RoleName => SubAssembly.Role?.Name ?? string.Empty;

    /// <summary>Pieces one batch makes. Informational only — expected to differ across a product's usages.</summary>
    public decimal Yield { get; }

    public decimal CostPerPiece { get; }

    public IReadOnlyList<RecipeIngredientRow> IngredientRows { get; }

    /// <summary>The lines' shares added up, for the table's Total row: 100% whenever the sub-assembly costs anything.</summary>
    public decimal PercentTotal => IngredientRows.Sum(r => r.PercentOfSection);

    /// <summary>E.g. "Filling · 25 g per piece · yields 40".</summary>
    public string DetailsText => $"{RoleName} · {WeightPerPiece:#,0.##} g per piece · yields {Yield:#,0.##}";

    /// <summary>For the Unit Conversion Overview (Controls/UnitConversionOverview): one batch of the sub-assembly...</summary>
    public string UnitName => $"{Name} batch";

    /// <summary>...and the pieces it makes.</summary>
    public string BaseEquivalentText => $"{Yield:#,0.##} pc";
}

/// <summary>One ingredient line of a sub-assembly in the Recipe Structure, scaled to one finished piece.</summary>
public sealed class RecipeIngredientRow
{
    public RecipeIngredientRow(SubAssemblyLine line, decimal weightPerPiece, decimal subAssemblyTotalWeight, decimal sectionCostPerPiece)
    {
        var costPerGram = line.Ingredient?.BufferedCostPerBaseUnit ?? 0m;

        IngredientName = line.Ingredient?.Name ?? "?";
        Unit = line.Unit?.UnitOfMeasure?.Abbreviation ?? "g";
        QuantityPerPiece = ProductRules.CalculateLineQuantityPerPiece(line.Quantity, weightPerPiece, subAssemblyTotalWeight);
        UnitCost = costPerGram * UnitConversionRules.ToBaseQuantity(1m, line.Unit?.BaseUnitsPerUnit);
        CostPerPiece = ProductRules.CalculateLineCostPerPiece(line.Grams, costPerGram, weightPerPiece, subAssemblyTotalWeight);
        PercentOfSection = CostingRules.PercentOf(CostPerPiece, sectionCostPerPiece);
    }

    public string IngredientName { get; }

    /// <summary>The unit the line was entered in, or g.</summary>
    public string Unit { get; }

    /// <summary>How much of the line goes into one piece, in Unit.</summary>
    public decimal QuantityPerPiece { get; }

    /// <summary>What one Unit of the ingredient costs, buffered: what recipes are costed with.</summary>
    public decimal UnitCost { get; }

    public decimal CostPerPiece { get; }

    /// <summary>This line's share of its sub-assembly's cost per piece.</summary>
    public decimal PercentOfSection { get; }
}

/// <summary>An in-memory (not yet saved) packaging line, shown in the Recipe Structure's Packaging section: quantities in one of the material's units.</summary>
public class PackagingLineEdit(RawMaterial packagingMaterial, RawMaterialUnitOption unit, PackagingLineAuthoredField authoredField, decimal authoredValue)
{
    public RawMaterial PackagingMaterial { get; } = packagingMaterial;

    /// <summary>The unit QtyPerPiece and PiecesPerPackagingUnit are in: the material's base unit or one of its alternative units.</summary>
    public RawMaterialUnitOption Unit { get; } = unit;

    public PackagingLineAuthoredField AuthoredField { get; } = authoredField;

    public decimal AuthoredValue { get; } = authoredValue;

    private (decimal QtyPerPiece, decimal PiecesPerPackagingUnit) Reciprocal =>
        ProductRules.ReconcileReciprocal(AuthoredField, AuthoredValue);

    public decimal QtyPerPiece => Reciprocal.QtyPerPiece;

    public decimal PiecesPerPackagingUnit => Reciprocal.PiecesPerPackagingUnit;

    /// <summary>What one Unit of the material costs, buffered: what products are costed with.</summary>
    public decimal UnitCost => PackagingMaterial.BufferedCostPerBaseUnit * UnitConversionRules.ToBaseQuantity(1m, Unit.Unit?.BaseUnitsPerUnit);

    public decimal CostPerPiece => ProductRules.CalculatePackagingLineCostPerPiece(
        PackagingMaterial.BufferedCostPerBaseUnit, QtyPerPiece, Unit.Unit?.BaseUnitsPerUnit);
}

/// <summary>How a packaging line's amount is entered (PackagingLine.AuthoredField): an amount per piece, or pieces per unit.</summary>
public sealed record PackagingEntryOption(string Name, PackagingLineAuthoredField Field)
{
    public static IReadOnlyList<PackagingEntryOption> All { get; } =
    [
        new("Quantity per piece", PackagingLineAuthoredField.QtyPerPiece),
        new("Pieces per unit", PackagingLineAuthoredField.PiecesPerPackagingUnit),
    ];
}

/// <summary>A row in the Tab Links grid: a link plus the display name of the role it's for (or "Packaging").</summary>
public class TabLinkRow(ProductTabLink link)
{
    public ProductTabLink Link { get; } = link;

    public string TabName => Link.TabKind == ProductTabKind.Packaging ? "Packaging" : Link.SubAssemblyRole?.Name ?? "?";

    public string LinkedToProductName => Link.LinkedProduct?.Name ?? "?";
}

/// <summary>One slice of the material cost per piece: a sub-assembly, or all of the packaging. Percent is its share of the material cost.</summary>
public sealed record CostShareRow(string Name, decimal Amount, decimal Percent);

/// <summary>What the Recipe Usage Example worked out: the material cost of a number of pieces, by slice.</summary>
public sealed record RecipeUsageExampleResult(string Title, decimal TotalCost, IReadOnlyList<CostShareRow> Breakdown);

/// <summary>
/// A product's cost per piece, from its materials to its selling price (cost-plus, CostingRules), with
/// each part's share of the selling price. Worked out from the form, so unsaved edits show at once.
/// </summary>
public sealed class ProductCostSummary
{
    public static ProductCostSummary Empty { get; } = new([], [], 0m, 0m, 0m, 0m, 0m);

    public ProductCostSummary(
        IReadOnlyList<decimal> usageCostsPerPiece,
        IReadOnlyList<decimal> packagingCostsPerPiece,
        decimal laborCost,
        decimal overheadCost,
        decimal administrativeCost,
        decimal marginPercent,
        decimal vatPercent)
    {
        SubAssemblies = usageCostsPerPiece.Sum();
        Packaging = packagingCostsPerPiece.Sum();
        Material = ProductRules.CalculateMaterialCostPerPiece(usageCostsPerPiece, packagingCostsPerPiece);
        Labor = laborCost;
        Overhead = overheadCost;
        Administrative = administrativeCost;
        MarginPercent = marginPercent;
        VatPercent = vatPercent;
        (BaseCost, PreVatPrice, SellingPrice) = CostingRules.CalculateSellingPrice(
            Material, laborCost, overheadCost, administrativeCost, marginPercent, vatPercent);
    }

    public decimal SubAssemblies { get; }

    public decimal Packaging { get; }

    public decimal Material { get; }

    public decimal Labor { get; }

    public decimal Overhead { get; }

    public decimal Administrative { get; }

    /// <summary>The subtotal the markup goes on: materials, labor, overhead and administrative.</summary>
    public decimal BaseCost { get; }

    public decimal MarginPercent { get; }

    public decimal PreVatPrice { get; }

    public decimal VatPercent { get; }

    public decimal SellingPrice { get; }

    /// <summary>The markup: the profit on one piece, since VAT is collected for the government.</summary>
    public decimal Markup => PreVatPrice - BaseCost;

    public decimal Vat => SellingPrice - PreVatPrice;

    public decimal ProfitMarginPercent => CostingRules.CalculateProfitMarginPercent(BaseCost, PreVatPrice);

    public decimal SubAssembliesShare => CostingRules.PercentOf(SubAssemblies, SellingPrice);

    public decimal PackagingShare => CostingRules.PercentOf(Packaging, SellingPrice);

    public decimal LaborShare => CostingRules.PercentOf(Labor, SellingPrice);

    public decimal OverheadShare => CostingRules.PercentOf(Overhead, SellingPrice);

    public decimal AdministrativeShare => CostingRules.PercentOf(Administrative, SellingPrice);

    public decimal BaseCostShare => CostingRules.PercentOf(BaseCost, SellingPrice);

    public decimal MarkupShare => CostingRules.PercentOf(Markup, SellingPrice);

    public decimal VatShare => CostingRules.PercentOf(Vat, SellingPrice);

    /// <summary>100% whenever there's a price, for the total row.</summary>
    public decimal SellingPriceShare => CostingRules.PercentOf(SellingPrice, SellingPrice);
}

/// <summary>
/// The Products screen: a product's recipe (the sub-assemblies it uses at a weight per piece, and its
/// packaging), its costing and its Live Template links. The name, description, recipe, packaging and
/// costing are edited in memory, and Save writes them together (IProductRepository.SaveAsync).
/// </summary>
public class ProductsViewModel : ViewModelBase
{
    /// <summary>The details tabs along the bottom: Recipe, Costing, then Links &amp; Clone.</summary>
    public const int RecipeTab = 0;

    public const int LinksTab = 2;

    private const string DefaultHeaderCaption = "Build the product from sub-assemblies and packaging, then set how it's priced.";

    private readonly IProductRepository _productRepository;
    private readonly IProductSubAssemblyUsageRepository _usageRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingParametersRepository _costingParametersRepository;
    private readonly IProductTabLinkRepository _tabLinkRepository;
    private readonly IProductCloner _productCloner;
    private readonly ISubAssemblyLineRepository _subAssemblyLineRepository;
    private readonly IValidator<Product> _productValidator;
    private readonly IValidator<CostingParameters> _costingParametersValidator;
    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<Product> _products;
    private readonly ReadOnlyObservableCollection<Product> _visibleProducts;

    /// <summary>Every product, for the link and clone pickers.</summary>
    public ReadOnlyObservableCollection<Product> Products => _products;

    /// <summary>The products the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<Product> VisibleProducts => _visibleProducts;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    public ObservableCollection<SubAssembly> SubAssemblies { get; } = [];

    /// <summary>The packaging materials only: ingredients never go on a packaging line.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>
    /// Every role's sub-assembly usages, saved or not, in recipe order (ProductUsageEdit.RecipeOrder). A
    /// linked role's usages come from the product it's linked to.
    /// </summary>
    public ObservableCollection<ProductUsageEdit> UsageEdits { get; } = [];

    public ObservableCollection<PackagingLineEdit> LineEdits { get; } = [];

    public ObservableCollection<TabLinkRow> TabLinks { get; } = [];

    public ObservableCollection<Product> LinkTargetProductOptions { get; } = [];

    /// <summary>Each sub-assembly's, and the packaging's, share of the material cost per piece: the Recipe Cost Overview.</summary>
    public ObservableCollection<CostShareRow> CostShares { get; } = [];

    /// <summary>The roles a sub-assembly can be added to: every role whose tab isn't linked.</summary>
    public ObservableCollection<SubAssemblyRole> UsageFormRoles { get; } = [];

    /// <summary>
    /// The form's role's sub-assemblies the product doesn't use yet, and only those with lines: one with
    /// none weighs nothing, so its cost can't be shared out per piece.
    /// </summary>
    public ObservableCollection<SubAssembly> UsageFormSubAssemblies { get; } = [];

    /// <summary>The picked packaging material's units, for the packaging form's unit picker.</summary>
    public ObservableCollection<RawMaterialUnitOption> EntryUnitOptions { get; } = [];

    public IReadOnlyList<PackagingEntryOption> EntryOptions => PackagingEntryOption.All;

    public IReadOnlyList<ProductTabKind> TabKindOptions { get; } = Enum.GetValues<ProductTabKind>();

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    private Product? _selectedProduct;
    public Product? SelectedProduct
    {
        get => _selectedProduct;
        set => this.RaiseAndSetIfChanged(ref _selectedProduct, value);
    }

    private int _editingId;

    /// <summary>The product whose recipe, packaging, costing and links the form holds, or null for a new product.</summary>
    private int? _detailProductId;

    private string _editingName = string.Empty;
    public string EditingName
    {
        get => _editingName;
        set => this.RaiseAndSetIfChanged(ref _editingName, value);
    }

    private string _editingDescription = string.Empty;
    public string EditingDescription
    {
        get => _editingDescription;
        set => this.RaiseAndSetIfChanged(ref _editingDescription, value);
    }

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The summary header's big title: the product's name, or "New product".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string> _headerInitial;
    public string HeaderInitial => _headerInitial.Value;

    private readonly ObservableAsPropertyHelper<string> _headerCaption;
    /// <summary>The description under the title, or what the screen is for while there's none.</summary>
    public string HeaderCaption => _headerCaption.Value;

    /// <summary>The day the product was last saved, e.g. "Sep 14, 2026", or "—".</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
    }

    /// <summary>The time of day it was last saved, e.g. "10:24 AM", or empty.</summary>
    private string _lastUpdatedTimeText = string.Empty;
    public string LastUpdatedTimeText
    {
        get => _lastUpdatedTimeText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedTimeText, value);
    }

    /// <summary>The details tab showing. Moves to Recipe when Links &amp; Clone is about to hide (ShowLinks).</summary>
    private int _selectedDetailsTab;
    public int SelectedDetailsTab
    {
        get => _selectedDetailsTab;
        set => this.RaiseAndSetIfChanged(ref _selectedDetailsTab, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _showLinks;
    /// <summary>A saved product: links and clones need one, so the tab only shows then.</summary>
    public bool ShowLinks => _showLinks.Value;

    private bool _isPackagingLinked;
    public bool IsPackagingLinked
    {
        get => _isPackagingLinked;
        private set => this.RaiseAndSetIfChanged(ref _isPackagingLinked, value);
    }

    private string _packagingLinkedToText = string.Empty;
    public string PackagingLinkedToText
    {
        get => _packagingLinkedToText;
        private set => this.RaiseAndSetIfChanged(ref _packagingLinkedToText, value);
    }

    private bool _hasUsages;
    public bool HasUsages
    {
        get => _hasUsages;
        private set => this.RaiseAndSetIfChanged(ref _hasUsages, value);
    }

    private bool _hasPackaging;
    public bool HasPackaging
    {
        get => _hasPackaging;
        private set => this.RaiseAndSetIfChanged(ref _hasPackaging, value);
    }

    private bool _hasRecipe;
    /// <summary>Whether there's a sub-assembly or packaging line at all: the Recipe Structure's empty state shows otherwise.</summary>
    public bool HasRecipe
    {
        get => _hasRecipe;
        private set => this.RaiseAndSetIfChanged(ref _hasRecipe, value);
    }

    private bool _hasMaterialCost;
    /// <summary>Whether there's any material cost to share out: the Recipe Cost Overview's chart shows only then.</summary>
    public bool HasMaterialCost
    {
        get => _hasMaterialCost;
        private set => this.RaiseAndSetIfChanged(ref _hasMaterialCost, value);
    }

    private ProductCostSummary _costs = ProductCostSummary.Empty;
    public ProductCostSummary Costs
    {
        get => _costs;
        private set => this.RaiseAndSetIfChanged(ref _costs, value);
    }

    // The add/edit sub-assembly form: the usage it edits (null when it adds one), and its fields.
    private ProductUsageEdit? _editingUsage;

    private bool _isUsageFormOpen;
    public bool IsUsageFormOpen
    {
        get => _isUsageFormOpen;
        private set => this.RaiseAndSetIfChanged(ref _isUsageFormOpen, value);
    }

    private string _usageFormTitle = "Add Sub-Assembly";
    public string UsageFormTitle
    {
        get => _usageFormTitle;
        private set => this.RaiseAndSetIfChanged(ref _usageFormTitle, value);
    }

    /// <summary>Adding, not editing: an edited usage keeps its role and sub-assembly, and only its weight changes.</summary>
    private bool _isAddingUsage = true;
    public bool IsAddingUsage
    {
        get => _isAddingUsage;
        private set => this.RaiseAndSetIfChanged(ref _isAddingUsage, value);
    }

    /// <summary>Whether any role's tab isn't linked, so a sub-assembly can be added to it.</summary>
    private bool _canAddUsage;
    public bool CanAddUsage
    {
        get => _canAddUsage;
        private set => this.RaiseAndSetIfChanged(ref _canAddUsage, value);
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

    private readonly ObservableAsPropertyHelper<string> _usageFormPreviewText;
    /// <summary>What the picked sub-assembly comes to at the typed weight, e.g. "A 1,200 g batch makes 24 pieces · ₱0.5000 a piece".</summary>
    public string UsageFormPreviewText => _usageFormPreviewText.Value;

    // The add/edit packaging form: the line it edits (null when it adds one), and its fields.
    private PackagingLineEdit? _editingLine;

    private bool _isLineFormOpen;
    public bool IsLineFormOpen
    {
        get => _isLineFormOpen;
        private set => this.RaiseAndSetIfChanged(ref _isLineFormOpen, value);
    }

    private string _lineFormTitle = "Add Packaging";
    public string LineFormTitle
    {
        get => _lineFormTitle;
        private set => this.RaiseAndSetIfChanged(ref _lineFormTitle, value);
    }

    private RawMaterial? _editingLinePackagingMaterial;
    public RawMaterial? EditingLinePackagingMaterial
    {
        get => _editingLinePackagingMaterial;
        set => this.RaiseAndSetIfChanged(ref _editingLinePackagingMaterial, value);
    }

    private RawMaterialUnitOption? _editingLineUnit;
    public RawMaterialUnitOption? EditingLineUnit
    {
        get => _editingLineUnit;
        set => this.RaiseAndSetIfChanged(ref _editingLineUnit, value);
    }

    private PackagingEntryOption? _editingLineEntry = PackagingEntryOption.All[0];
    public PackagingEntryOption? EditingLineEntry
    {
        get => _editingLineEntry;
        set => this.RaiseAndSetIfChanged(ref _editingLineEntry, value);
    }

    private decimal _editingLineAuthoredValue;
    public decimal EditingLineAuthoredValue
    {
        get => _editingLineAuthoredValue;
        set => this.RaiseAndSetIfChanged(ref _editingLineAuthoredValue, value);
    }

    private readonly ObservableAsPropertyHelper<string> _lineValueLabel;
    /// <summary>What the packaging form's value is: "cm per piece" or "Pieces per box".</summary>
    public string LineValueLabel => _lineValueLabel.Value;

    private readonly ObservableAsPropertyHelper<string> _lineFormPreviewText;
    /// <summary>What the typed line costs a piece, e.g. "₱0.5715 a piece".</summary>
    public string LineFormPreviewText => _lineFormPreviewText.Value;

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

    private decimal _editingAdministrativeCost;
    public decimal EditingAdministrativeCost
    {
        get => _editingAdministrativeCost;
        set => this.RaiseAndSetIfChanged(ref _editingAdministrativeCost, value);
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

    // Recipe Usage Example.
    private decimal _exampleQuantity = 1m;
    public decimal ExampleQuantity
    {
        get => _exampleQuantity;
        set => this.RaiseAndSetIfChanged(ref _exampleQuantity, value);
    }

    private RecipeUsageExampleResult? _exampleResult;
    public RecipeUsageExampleResult? ExampleResult
    {
        get => _exampleResult;
        private set => this.RaiseAndSetIfChanged(ref _exampleResult, value);
    }

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

    // What the product had when it was loaded, so Save only sends the parts that changed.
    private Dictionary<int, List<(int SubAssemblyId, decimal WeightPerPiece)>> _savedUsagesByRole = [];
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedLines = [];
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) _savedCosting;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> EditUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> RemoveUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ApplyUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ApplyLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; }
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
        ISubAssemblyLineRepository subAssemblyLineRepository,
        ISubAssemblyRoleRepository subAssemblyRoleRepository,
        IRawMaterialRepository rawMaterialRepository,
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
        _subAssemblyLineRepository = subAssemblyLineRepository;
        _productValidator = productValidator;
        _costingParametersValidator = costingParametersValidator;

        var byName = SortExpressionComparer<Product>.Ascending(p => p.Name);

        _productCache.Connect()
            .SortAndBind(out _products, byName)
            .Subscribe();

        var nameFilter = this.WhenAnyValue(x => x.SearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Select(text => (Func<Product, bool>)(p => text.Length == 0 || p.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)));

        _productCache.Connect()
            .Filter(nameFilter)
            .SortAndBind(out _visibleProducts, byName)
            .Subscribe();

        _headerTitle = this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingName, (product, name) =>
                product is null && string.IsNullOrWhiteSpace(name) ? "New product" : name)
            .ToProperty(this, x => x.HeaderTitle, "New product");

        _headerInitial = this.WhenAnyValue(x => x.HeaderTitle)
            .Select(title => string.IsNullOrWhiteSpace(title) ? "?" : title.Trim()[..1].ToUpperInvariant())
            .ToProperty(this, x => x.HeaderInitial, "N");

        _headerCaption = this.WhenAnyValue(x => x.EditingDescription)
            .Select(description => string.IsNullOrWhiteSpace(description) ? DefaultHeaderCaption : description.Trim())
            .ToProperty(this, x => x.HeaderCaption, DefaultHeaderCaption);

        // A hidden tab can't stay the one showing: leaving a saved product while on Links & Clone goes
        // back to Recipe. Subscribed before ShowLinks, so the tab strip moves before the tab hides; a
        // TabControlExt whose selected tab hides picks a neighbour itself.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is null && SelectedDetailsTab == LinksTab)
            .Subscribe(_ => SelectedDetailsTab = RecipeTab);

        _showLinks = this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => product is not null)
            .ToProperty(this, x => x.ShowLinks);

        _usageFormPreviewText = this.WhenAnyValue(x => x.EditingUsageSubAssembly, x => x.EditingUsageWeightPerPiece, (subAssembly, weight) =>
                subAssembly is { TotalWeight: > 0 } && weight > 0
                    ? $"A {subAssembly.TotalWeight:#,0.##} g batch makes {ProductRules.CalculateYield(subAssembly.TotalWeight, weight):#,0.##} pieces · "
                      + $"₱{ProductRules.CalculateUsageCostPerPiece(subAssembly.TotalCost, weight, subAssembly.TotalWeight):N4} a piece"
                    : string.Empty)
            .ToProperty(this, x => x.UsageFormPreviewText, string.Empty);

        _lineValueLabel = this.WhenAnyValue(x => x.EditingLineEntry, x => x.EditingLineUnit, (entry, unit) =>
                entry?.Field == PackagingLineAuthoredField.PiecesPerPackagingUnit
                    ? $"Pieces per {unit?.Abbreviation ?? "unit"}"
                    : $"{unit?.Abbreviation ?? "Quantity"} per piece")
            .ToProperty(this, x => x.LineValueLabel, "Quantity per piece");

        _lineFormPreviewText = this.WhenAnyValue(
                x => x.EditingLinePackagingMaterial, x => x.EditingLineUnit, x => x.EditingLineEntry, x => x.EditingLineAuthoredValue,
                (material, unit, entry, value) =>
                    material is not null && unit is not null && entry is not null && value > 0
                        ? $"₱{new PackagingLineEdit(material, unit, entry.Field, value).CostPerPiece:N4} a piece"
                        : string.Empty)
            .ToProperty(this, x => x.LineFormPreviewText, string.Empty);

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which only
        // re-points a selection at its refreshed copy; the form, recipe and unsaved edits stay as-is.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is not null && !IsRefreshing)
            .Subscribe(product => LoadIntoForm(product!));

        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(_ => !IsRefreshing)
            .Subscribe(ResetDetails);

        // A newer selection cancels an older one still loading (Switch disposes it, which cancels its token).
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(_ => !IsRefreshing)
            .Select(product => Observable.FromAsync(cancellationToken => LoadProductDetailAsync(product, cancellationToken)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.EditingUsageRole)
            .Where(_ => !IsRefreshing)
            .Subscribe(_ => RefreshUsageFormSubAssemblies());

        this.WhenAnyValue(x => x.EditingLinePackagingMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material => ReplaceEntryUnitOptions(material, keepUnitId: null));

        this.WhenAnyValue(
                x => x.EditingLaborCost, x => x.EditingOverheadCost, x => x.EditingAdministrativeCost, x => x.EditingMarginPercent, x => x.EditingVatPercent)
            .Subscribe(_ => RecomputeCosts());

        // A worked-out example belongs to the quantity it was worked out for.
        this.WhenAnyValue(x => x.ExampleQuantity)
            .Subscribe(_ => ExampleResult = null);

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, pickers and unsaved edits while
        // picking up what changed elsewhere, such as a repriced ingredient flowing into a sub-assembly.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await subAssemblyRoleRepository.GetAllAsync(CancellationToken.None);
            var subAssemblies = await subAssemblyRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await rawMaterialRepository.GetAllAsync(CancellationToken.None);
            var products = await _productRepository.GetAllAsync(CancellationToken.None);

            // The recipe's sub-assemblies can change on other tabs (a repriced ingredient, an edited
            // line), and the tab links show role and product names, so both are fetched again too.
            var detailProductId = _detailProductId;
            HashSet<int> refetchedSubAssemblyIds = [.. UsageEdits.Select(u => u.SubAssembly.Id)];
            var lines = await _subAssemblyLineRepository.GetForSubAssembliesAsync(refetchedSubAssemblyIds, CancellationToken.None);
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
                PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
                _productCache.MergeFrom(products);

                SelectedProduct = Products.FirstOrDefault(p => p.Id == selectedId);
                if (SelectedProduct is not null)
                {
                    ShowUpdatedAt(SelectedProduct.UpdatedAt);
                }

                if (detailProductId is not null && detailProductId == _detailProductId)
                {
                    ReplaceTabLinks(links);
                }

                RefreshUnsavedRows(lines, refetchedSubAssemblyIds);

                RefreshUsageFormRoles();
                EditingUsageRole = UsageFormRoles.FirstOrDefault(r => r.Id == usageRoleId);
                RefreshUsageFormSubAssemblies();
                EditingUsageSubAssembly = UsageFormSubAssemblies.FirstOrDefault(s => s.Id == usageSubAssemblyId);

                var previousLineMaterial = EditingLinePackagingMaterial;
                EditingLinePackagingMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == linePackagingMaterialId);
                if (!ReferenceEquals(previousLineMaterial, EditingLinePackagingMaterial))
                {
                    ReplaceEntryUnitOptions(EditingLinePackagingMaterial, lineUnitId);
                }

                EditingLinkRole = Roles.FirstOrDefault(r => r.Id == linkRoleId);
                RefreshLinkTargetProductOptions();
                EditingLinkTargetProduct = LinkTargetProductOptions.FirstOrDefault(p => p.Id == linkTargetId);

                RecomputeRecipe();
            });
        });

        NewCommand = ReactiveCommand.Create(StartNewProduct);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            // A form left open is part of what's being saved.
            if (IsUsageFormOpen)
            {
                await ApplyUsageFormAsync();
            }

            if (IsLineFormOpen)
            {
                ApplyLineForm();
            }

            var product = new Product
            {
                Id = _editingId,
                Name = EditingName,
                Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription.Trim(),
            };

            var validation = _productValidator.Validate(product);
            if (!validation.IsValid)
            {
                ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            var costing = new CostingParameters
            {
                LaborCost = EditingLaborCost,
                OverheadCost = EditingOverheadCost,
                AdministrativeCost = EditingAdministrativeCost,
                MarginPercent = EditingMarginPercent,
                VatPercent = EditingVatPercent,
            };

            var costingValidation = _costingParametersValidator.Validate(costing);
            if (!costingValidation.IsValid)
            {
                ErrorMessage = string.Join(" ", costingValidation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            var id = await _productRepository.SaveAsync(
                product, ChangedRoles(), ChangedLines(), CurrentCosting() == _savedCosting ? null : costing, CancellationToken.None);

            // The list picks up the new name; then the form reloads what was saved, which is what the
            // next Save compares against.
            await LoadCommand.Execute();
            await ReloadSavedProductAsync(id);
        }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync($"Delete '{SelectedProduct!.Name}'? This can't be undone.", async () =>
            {
                await _productRepository.DeleteAsync(SelectedProduct!.Id, CancellationToken.None);
                StartNewProduct();
                await LoadCommand.Execute();
            }),
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        AddUsageCommand = ReactiveCommand.Create(
            () =>
            {
                _editingUsage = null;
                UsageFormTitle = "Add Sub-Assembly";
                IsAddingUsage = true;
                EditingUsageRole = UsageFormRoles.FirstOrDefault();
                RefreshUsageFormSubAssemblies();
                EditingUsageSubAssembly = null;
                EditingUsageWeightPerPiece = 0m;
                IsUsageFormOpen = true;
            },
            this.WhenAnyValue(x => x.CanAddUsage));

        EditUsageCommand = ReactiveCommand.Create<ProductUsageEdit>(usage =>
        {
            _editingUsage = usage;
            UsageFormTitle = $"Edit {usage.Name}";
            IsAddingUsage = false;
            EditingUsageRole = UsageFormRoles.FirstOrDefault(r => r.Id == usage.SubAssembly.RoleId);
            RefreshUsageFormSubAssemblies();
            EditingUsageSubAssembly = UsageFormSubAssemblies.FirstOrDefault(s => s.Id == usage.SubAssembly.Id);
            EditingUsageWeightPerPiece = usage.WeightPerPiece;
            IsUsageFormOpen = true;
        });

        // Nothing to confirm: the usage only leaves the recipe, and is removed when the product is saved.
        RemoveUsageCommand = ReactiveCommand.Create<ProductUsageEdit>(usage =>
        {
            if (ReferenceEquals(usage, _editingUsage))
            {
                CloseUsageForm();
            }

            UsageEdits.Remove(usage);
            RefreshUsageFormSubAssemblies();
            RecomputeRecipe();
        });

        ApplyUsageCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(ApplyUsageFormAsync));

        CancelUsageCommand = ReactiveCommand.Create(CloseUsageForm);

        AddLineCommand = ReactiveCommand.Create(
            () =>
            {
                _editingLine = null;
                LineFormTitle = "Add Packaging";
                EditingLinePackagingMaterial = null;
                EditingLineEntry = PackagingEntryOption.All[0];
                EditingLineAuthoredValue = 0m;
                IsLineFormOpen = true;
            },
            this.WhenAnyValue(x => x.IsPackagingLinked).Select(linked => !linked));

        var canChangeSelectedLine = this.WhenAnyValue(x => x.SelectedLineEdit, x => x.IsPackagingLinked, (line, linked) => line is not null && !linked);

        EditLineCommand = ReactiveCommand.Create(
            () =>
            {
                var line = SelectedLineEdit!;
                _editingLine = line;
                LineFormTitle = $"Edit {line.PackagingMaterial.Name}";
                EditingLinePackagingMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == line.PackagingMaterial.Id) ?? line.PackagingMaterial;
                EditingLineUnit = EntryUnitOptions.FirstOrDefault(o => o.UnitId == line.Unit.UnitId) ?? EntryUnitOptions.FirstOrDefault();
                EditingLineEntry = PackagingEntryOption.All.First(o => o.Field == line.AuthoredField);
                EditingLineAuthoredValue = line.AuthoredValue;
                IsLineFormOpen = true;
            },
            canChangeSelectedLine);

        RemoveLineCommand = ReactiveCommand.Create(
            () =>
            {
                var line = SelectedLineEdit!;
                if (ReferenceEquals(line, _editingLine))
                {
                    CloseLineForm();
                }

                LineEdits.Remove(line);
                RecomputeRecipe();
            },
            canChangeSelectedLine);

        ApplyLineCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() =>
        {
            ApplyLineForm();
            return Task.CompletedTask;
        }));

        CancelLineCommand = ReactiveCommand.Create(CloseLineForm);

        CalculateExampleCommand = ReactiveCommand.Create(
            () =>
            {
                var pieces = ExampleQuantity;
                ExampleResult = new RecipeUsageExampleResult(
                    $"Cost for {pieces:#,0.##} pc of {HeaderTitle}",
                    Costs.Material * pieces,
                    [.. CostShares.Select(share => share with { Amount = share.Amount * pieces })]);
            },
            this.WhenAnyValue(x => x.ExampleQuantity).Select(quantity => quantity > 0));

        // Linking and unlinking are saved at once. Only the part concerned reloads, so unsaved edits to
        // the rest of the product stay.
        LinkTabCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var roleId = EditingLinkTabKind == ProductTabKind.SubAssemblyRole ? EditingLinkRole!.Id : (int?)null;
                await _tabLinkRepository.SetLinkAsync(SelectedProduct!.Id, EditingLinkTabKind, roleId, EditingLinkTargetProduct!.Id, CancellationToken.None);
                await ReloadPartAsync(EditingLinkTabKind, roleId);
            }),
            this.WhenAnyValue(
                x => x.SelectedProduct, x => x.EditingLinkTabKind, x => x.EditingLinkRole, x => x.EditingLinkTargetProduct,
                (product, tabKind, role, target) => product is not null && target is not null && (tabKind == ProductTabKind.Packaging || role is not null)));

        UnlinkTabCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var link = SelectedTabLink!.Link;
                await _tabLinkRepository.RemoveLinkAsync(SelectedProduct!.Id, link.TabKind, link.SubAssemblyRoleId, CancellationToken.None);
                await ReloadPartAsync(link.TabKind, link.SubAssemblyRoleId);
            }),
            this.WhenAnyValue(x => x.SelectedTabLink).Select(link => link is not null));

        // A clone copies the product as saved: unsaved edits stay with this one.
        CloneCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var newId = await _productCloner.CloneAsync(SelectedProduct!.Id, EditingCloneName, CancellationToken.None);
                await LoadCommand.Execute();
                SelectedProduct = Products.FirstOrDefault(p => p.Id == newId);
            }),
            this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingCloneName, (product, name) => product is not null && !string.IsNullOrWhiteSpace(name)));
    }

    /// <summary>The Last Updated tile: the day on one line, the time under it; "—" for a product never saved since it was added.</summary>
    private void ShowUpdatedAt(DateTime? updatedAt)
    {
        LastUpdatedText = updatedAt?.ToString("MMM d, yyyy") ?? "—";
        LastUpdatedTimeText = updatedAt?.ToString("h:mm tt") ?? string.Empty;
    }

    private void LoadIntoForm(Product product)
    {
        _editingId = product.Id;
        EditingName = product.Name;
        EditingDescription = product.Description ?? string.Empty;
        ShowUpdatedAt(product.UpdatedAt);
        EditingCloneName = $"{product.Name} Copy";
    }

    /// <summary>
    /// What follows a new selection: nothing picked or open. Another product's recipe is cleared while
    /// its own loads; the same product picked again (a fresh copy after Save) keeps showing until then.
    /// </summary>
    private void ResetDetails(Product? product)
    {
        CloseUsageForm();
        CloseLineForm();
        SelectedLineEdit = null;
        SelectedTabLink = null;
        ExampleResult = null;
        RefreshLinkTargetProductOptions();

        if (product?.Id != _detailProductId)
        {
            ClearDetails();
        }
    }

    /// <summary>A new product: no name, recipe, packaging, costing or links, and nothing selected.</summary>
    private void StartNewProduct()
    {
        SelectedProduct = null;
        _editingId = 0;
        EditingName = string.Empty;
        EditingDescription = string.Empty;
        ShowUpdatedAt(null);
        EditingCloneName = string.Empty;

        // After an unsaved new product nothing was selected, so the selection subscriptions don't run.
        CloseUsageForm();
        CloseLineForm();
        SelectedLineEdit = null;
        SelectedTabLink = null;
        ClearDetails();
    }

    /// <summary>Empties the recipe, packaging, costing and links, and what Save compares them against.</summary>
    private void ClearDetails()
    {
        _detailProductId = null;
        UsageEdits.Clear();
        LineEdits.Clear();
        ReplaceTabLinks([]);
        _savedUsagesByRole = [];
        _savedLines = [];
        SetCosting(null);
        RefreshUsageFormRoles();
        RecomputeRecipe();
    }

    private async Task LoadProductDetailAsync(Product? product, CancellationToken cancellationToken)
    {
        if (product is null)
        {
            return;
        }

        var links = await _tabLinkRepository.GetForProductAsync(product.Id, cancellationToken);
        var usages = await _usageRepository.GetEffectiveForProductAsync(product.Id, cancellationToken);
        var lines = await _packagingLineRepository.GetEffectiveForProductAsync(product.Id, cancellationToken);
        var costing = await _costingParametersRepository.GetForProductAsync(product.Id, cancellationToken);
        var subAssemblyLines = await _subAssemblyLineRepository.GetForSubAssembliesAsync(
            [.. usages.Select(u => u.SubAssemblyId).Distinct()], cancellationToken);

        // Only the product picked last is shown; a newer pick cancels this load.
        cancellationToken.ThrowIfCancellationRequested();

        _detailProductId = product.Id;
        ReplaceTabLinks(links);
        ReplaceUsages(usages, subAssemblyLines);
        ReplaceLines(lines);
        SetCosting(costing);
        RefreshUsageFormRoles();
        RecomputeRecipe();
    }

    /// <summary>
    /// After a tab was linked or unlinked: the links, and the part concerned — one role's usages or the
    /// packaging lines — as the product now shows it, which is also what Save compares that part against.
    /// </summary>
    private async Task ReloadPartAsync(ProductTabKind tabKind, int? roleId)
    {
        var productId = SelectedProduct!.Id;
        var links = await _tabLinkRepository.GetForProductAsync(productId, CancellationToken.None);

        CloseUsageForm();
        CloseLineForm();

        if (tabKind == ProductTabKind.SubAssemblyRole)
        {
            var usages = await _usageRepository.GetEffectiveForRoleAsync(productId, roleId!.Value, CancellationToken.None);
            var subAssemblyLines = await _subAssemblyLineRepository.GetForSubAssembliesAsync(
                [.. usages.Select(u => u.SubAssemblyId).Distinct()], CancellationToken.None);

            ReplaceTabLinks(links);
            foreach (var stale in UsageEdits.Where(u => u.SubAssembly.RoleId == roleId).ToList())
            {
                UsageEdits.Remove(stale);
            }

            foreach (var usage in BuildUsageEdits(usages, subAssemblyLines))
            {
                InsertInRecipeOrder(usage);
            }

            var own = OwnUsagesByRole();
            if (own.TryGetValue(roleId.Value, out var roleUsages))
            {
                _savedUsagesByRole[roleId.Value] = roleUsages;
            }
            else
            {
                _savedUsagesByRole.Remove(roleId.Value);
            }
        }
        else
        {
            var lines = await _packagingLineRepository.GetEffectiveForProductAsync(productId, CancellationToken.None);
            ReplaceTabLinks(links);
            ReplaceLines(lines);
        }

        RefreshUsageFormRoles();
        RecomputeRecipe();
    }

    /// <summary>The usages as rows, each with its sub-assembly's lines and the link its role reads through, in recipe order.</summary>
    private IEnumerable<ProductUsageEdit> BuildUsageEdits(IReadOnlyList<ProductSubAssemblyUsage> usages, IReadOnlyList<SubAssemblyLine> subAssemblyLines)
    {
        var linesBySubAssembly = subAssemblyLines.ToLookup(l => l.SubAssemblyId);
        return usages
            .Select(u => new ProductUsageEdit(u.SubAssembly!, u.WeightPerPiece, [.. linesBySubAssembly[u.SubAssemblyId]], LinkForRole(u.SubAssembly!.RoleId)))
            .Order(ProductUsageEdit.RecipeOrder);
    }

    private void ReplaceUsages(IReadOnlyList<ProductSubAssemblyUsage> usages, IReadOnlyList<SubAssemblyLine> subAssemblyLines)
    {
        UsageEdits.Clear();
        foreach (var usage in BuildUsageEdits(usages, subAssemblyLines))
        {
            UsageEdits.Add(usage);
        }

        _savedUsagesByRole = OwnUsagesByRole();
    }

    private void ReplaceLines(IReadOnlyList<PackagingLine> lines)
    {
        SelectedLineEdit = null;
        LineEdits.Clear();
        foreach (var line in lines)
        {
            LineEdits.Add(new PackagingLineEdit(
                line.PackagingMaterial!,
                new RawMaterialUnitOption(line.Unit, line.PackagingMaterial!),
                line.AuthoredField,
                line.AuthoredField == PackagingLineAuthoredField.QtyPerPiece ? line.QtyPerPiece : line.PiecesPerPackagingUnit));
        }

        _savedLines = CurrentLines();
    }

    private void SetCosting(CostingParameters? costing)
    {
        EditingLaborCost = costing?.LaborCost ?? 0m;
        EditingOverheadCost = costing?.OverheadCost ?? 0m;
        EditingAdministrativeCost = costing?.AdministrativeCost ?? 0m;
        EditingMarginPercent = costing?.MarginPercent ?? 0m;
        EditingVatPercent = costing?.VatPercent ?? 0m;
        _savedCosting = CurrentCosting();
    }

    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) CurrentCosting() =>
        (EditingLaborCost, EditingOverheadCost, EditingAdministrativeCost, EditingMarginPercent, EditingVatPercent);

    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> CurrentLines() =>
        [.. LineEdits.Select(l => (l.PackagingMaterial.Id, l.Unit.UnitId, l.AuthoredField, l.AuthoredValue))];

    /// <summary>The product's own usages by role. A linked role's usages belong to another product, and are never saved here.</summary>
    private Dictionary<int, List<(int SubAssemblyId, decimal WeightPerPiece)>> OwnUsagesByRole() =>
        UsageEdits
            .Where(u => !u.IsLinked)
            .GroupBy(u => u.SubAssembly.RoleId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(u => (SubAssemblyId: u.SubAssembly.Id, u.WeightPerPiece)).OrderBy(u => u.SubAssemblyId).ToList());

    /// <summary>Each unlinked role whose usages differ from those last loaded, with its new set (empty when they were all removed).</summary>
    private List<(int SubAssemblyRoleId, IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> Usages)> ChangedRoles()
    {
        var current = OwnUsagesByRole();
        var changed = new List<(int, IReadOnlyList<(int, decimal)>)>();
        foreach (var roleId in current.Keys.Union(_savedUsagesByRole.Keys))
        {
            if (LinkForRole(roleId) is not null)
            {
                continue;
            }

            var now = current.GetValueOrDefault(roleId) ?? [];
            var before = _savedUsagesByRole.GetValueOrDefault(roleId) ?? [];
            if (!now.SequenceEqual(before))
            {
                changed.Add((roleId, now));
            }
        }

        return changed;
    }

    /// <summary>The packaging lines when they differ from those last loaded, else null. A linked Packaging tab's lines are never saved here.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? ChangedLines()
    {
        if (IsPackagingLinked)
        {
            return null;
        }

        var current = CurrentLines();
        return current.SequenceEqual(_savedLines) ? null : current;
    }

    /// <summary>Loads the saved product again and selects the fresh copy, so the form shows (and compares against) what was saved.</summary>
    private async Task ReloadSavedProductAsync(int id)
    {
        var saved = await _productRepository.GetByIdAsync(id, CancellationToken.None);
        if (saved is not null)
        {
            _productCache.AddOrUpdate(saved);
            SelectedProduct = saved;
        }
    }

    private async Task ApplyUsageFormAsync()
    {
        var subAssembly = EditingUsageSubAssembly
            ?? throw new InvalidOperationException("Pick a sub-assembly, or cancel the form.");
        if (EditingUsageWeightPerPiece <= 0)
        {
            throw new InvalidOperationException($"Enter how many grams of {subAssembly.Name} go into one piece.");
        }

        // An edited usage keeps its sub-assembly, so its lines are already here.
        var lines = _editingUsage is not null && _editingUsage.SubAssembly.Id == subAssembly.Id
            ? _editingUsage.Lines
            : await _subAssemblyLineRepository.GetForSubAssembliesAsync([subAssembly.Id], CancellationToken.None);

        var usage = new ProductUsageEdit(_editingUsage?.SubAssembly ?? subAssembly, EditingUsageWeightPerPiece, lines, link: null);
        if (_editingUsage is not null)
        {
            UsageEdits.Remove(_editingUsage);
        }

        InsertInRecipeOrder(usage);
        CloseUsageForm();
        RecomputeRecipe();
    }

    private void CloseUsageForm()
    {
        IsUsageFormOpen = false;
        _editingUsage = null;
    }

    private void ApplyLineForm()
    {
        var material = EditingLinePackagingMaterial
            ?? throw new InvalidOperationException("Pick a packaging material, or cancel the form.");
        var unit = EditingLineUnit ?? throw new InvalidOperationException($"Pick the unit {material.Name} is measured in.");
        var entry = EditingLineEntry ?? throw new InvalidOperationException("Pick how the amount is entered.");
        if (EditingLineAuthoredValue <= 0)
        {
            throw new InvalidOperationException($"Enter the {LineValueLabel.ToLowerInvariant()}.");
        }

        if (LineEdits.Any(l => !ReferenceEquals(l, _editingLine) && l.PackagingMaterial.Id == material.Id))
        {
            throw new InvalidOperationException(
                $"{material.Name} is already in the packaging. A product can't use the same packaging material more than once — edit its line instead.");
        }

        var line = new PackagingLineEdit(material, unit, entry.Field, EditingLineAuthoredValue);
        var index = _editingLine is null ? -1 : LineEdits.IndexOf(_editingLine);
        if (index >= 0)
        {
            LineEdits[index] = line;
        }
        else
        {
            LineEdits.Add(line);
        }

        SelectedLineEdit = line;
        CloseLineForm();
        RecomputeRecipe();
    }

    private void CloseLineForm()
    {
        IsLineFormOpen = false;
        _editingLine = null;
    }

    private void InsertInRecipeOrder(ProductUsageEdit usage)
    {
        var index = 0;
        while (index < UsageEdits.Count && ProductUsageEdit.RecipeOrder.Compare(UsageEdits[index], usage) <= 0)
        {
            index++;
        }

        UsageEdits.Insert(index, usage);
    }

    private ProductTabLink? LinkForRole(int roleId) =>
        TabLinks.Select(r => r.Link).FirstOrDefault(l => l.TabKind == ProductTabKind.SubAssemblyRole && l.SubAssemblyRoleId == roleId);

    /// <summary>Swaps in a fresh copy of the product's tab links (they show role and product names), keeping the selected link.</summary>
    private void ReplaceTabLinks(IReadOnlyList<ProductTabLink> links)
    {
        var selectedKind = SelectedTabLink?.Link.TabKind;
        var selectedRoleId = SelectedTabLink?.Link.SubAssemblyRoleId;

        TabLinks.Clear();
        TabLinks.AddRange(links.Select(l => new TabLinkRow(l)));
        SelectedTabLink = selectedKind is null
            ? null
            : TabLinks.FirstOrDefault(r => r.Link.TabKind == selectedKind && r.Link.SubAssemblyRoleId == selectedRoleId);

        var packagingLink = links.FirstOrDefault(l => l.TabKind == ProductTabKind.Packaging);
        IsPackagingLinked = packagingLink is not null;
        PackagingLinkedToText = packagingLink is null ? string.Empty : $"Linked to {packagingLink.LinkedProduct?.Name}";
    }

    /// <summary>The roles the sub-assembly form offers: those whose tab isn't linked.</summary>
    private void RefreshUsageFormRoles()
    {
        UsageFormRoles.MergeFrom([.. Roles.Where(r => LinkForRole(r.Id) is null)], r => r.Id);
        CanAddUsage = UsageFormRoles.Count > 0;
    }

    /// <summary>The form's role's sub-assemblies with lines that the product doesn't use yet (besides the one being edited).</summary>
    private void RefreshUsageFormSubAssemblies()
    {
        var role = EditingUsageRole;
        var editingId = _editingUsage?.SubAssembly.Id;
        var usedIds = UsageEdits.Where(u => !ReferenceEquals(u, _editingUsage)).Select(u => u.SubAssembly.Id).ToHashSet();

        UsageFormSubAssemblies.MergeFrom(
            [.. SubAssemblies.Where(s => role is not null && s.RoleId == role.Id && !usedIds.Contains(s.Id) && (s.TotalWeight > 0 || s.Id == editingId))],
            s => s.Id);

        if (EditingUsageSubAssembly is { } picked && !UsageFormSubAssemblies.Contains(picked))
        {
            EditingUsageSubAssembly = null;
        }
    }

    private void RefreshLinkTargetProductOptions()
    {
        LinkTargetProductOptions.MergeFrom(
            [.. SelectedProduct is null ? Products : Products.Where(p => p.Id != SelectedProduct.Id)],
            p => p.Id);
    }

    /// <summary>Fills the packaging form's unit picker with <paramref name="material"/>'s units and picks the one with <paramref name="keepUnitId"/>, else its base unit.</summary>
    private void ReplaceEntryUnitOptions(RawMaterial? material, int? keepUnitId)
    {
        EditingLineUnit = null;
        EntryUnitOptions.Clear();
        EntryUnitOptions.AddRange(RawMaterialUnitOption.For(material));
        EditingLineUnit = EntryUnitOptions.FirstOrDefault(o => o.UnitId == keepUnitId) ?? EntryUnitOptions.FirstOrDefault();
    }

    /// <summary>
    /// Points the recipe and packaging rows, saved or not, at the refreshed sub-assemblies, lines and
    /// materials, keeping each row's own numbers, so their costs reflect changes made on other tabs. A row
    /// whose sub-assembly, lines or material didn't change is left alone. A row whose lines weren't among
    /// those refetched (a product picked while the refresh ran) keeps its own.
    /// </summary>
    private void RefreshUnsavedRows(IReadOnlyList<SubAssemblyLine> subAssemblyLines, IReadOnlySet<int> refetchedSubAssemblyIds)
    {
        var linesBySubAssembly = subAssemblyLines.ToLookup(l => l.SubAssemblyId);
        for (var i = 0; i < UsageEdits.Count; i++)
        {
            var edit = UsageEdits[i];
            var subAssembly = SubAssemblies.FirstOrDefault(s => s.Id == edit.SubAssembly.Id);
            IReadOnlyList<SubAssemblyLine> lines = refetchedSubAssemblyIds.Contains(edit.SubAssembly.Id)
                ? [.. linesBySubAssembly[edit.SubAssembly.Id]]
                : edit.Lines;
            if (subAssembly is null
                || (CollectionMerge.HasSameValues(subAssembly, edit.SubAssembly)
                    && lines.Count == edit.Lines.Count
                    && lines.Zip(edit.Lines).All(pair => CollectionMerge.HasSameValues(pair.First, pair.Second))))
            {
                continue;
            }

            UsageEdits[i] = new ProductUsageEdit(subAssembly, edit.WeightPerPiece, lines, edit.Link);
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
            var options = RawMaterialUnitOption.For(material);
            var unit = options.FirstOrDefault(o => o.UnitId == edit.Unit.UnitId) ?? options[0];

            var wasSelected = ReferenceEquals(SelectedLineEdit, edit);
            LineEdits[i] = new PackagingLineEdit(material, unit, edit.AuthoredField, edit.AuthoredValue);
            if (wasSelected)
            {
                SelectedLineEdit = LineEdits[i];
            }
        }
    }

    /// <summary>After the recipe or packaging changed: each part's share of the material cost, and everything priced from them.</summary>
    private void RecomputeRecipe()
    {
        RecomputeCosts();

        var material = Costs.Material;
        CostShares.Clear();
        CostShares.AddRange(UsageEdits.Select(u => new CostShareRow(u.Name, u.CostPerPiece, CostingRules.PercentOf(u.CostPerPiece, material))));
        if (LineEdits.Count > 0)
        {
            CostShares.Add(new CostShareRow("Packaging", Costs.Packaging, CostingRules.PercentOf(Costs.Packaging, material)));
        }

        HasUsages = UsageEdits.Count > 0;
        HasPackaging = LineEdits.Count > 0;
        HasRecipe = HasUsages || HasPackaging;
        HasMaterialCost = material > 0;
        ExampleResult = null;
    }

    /// <summary>The cost summary, from the recipe and packaging as they are now and the costing fields.</summary>
    private void RecomputeCosts() =>
        Costs = new ProductCostSummary(
            [.. UsageEdits.Select(u => u.CostPerPiece)],
            [.. LineEdits.Select(l => l.CostPerPiece)],
            EditingLaborCost,
            EditingOverheadCost,
            EditingAdministrativeCost,
            EditingMarginPercent,
            EditingVatPercent);
}
