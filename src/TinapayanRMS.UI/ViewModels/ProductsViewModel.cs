using System.Collections.ObjectModel;
using System.Collections.Specialized;
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

/// <summary>One ingredient line of a sub-assembly, saved or not: grams of an ingredient, costed at its buffered cost per gram.</summary>
public sealed class SubAssemblyLineEdit(RawMaterial ingredient, decimal grams, bool isFormulaBase = false, decimal? authoredBakersPercent = null)
{
    public RawMaterial Ingredient { get; } = ingredient;

    public decimal Grams { get; } = grams;

    /// <summary>Part of the formula's 100% base, which every line's baker's percentage is read against.</summary>
    public bool IsFormulaBase { get; } = isFormulaBase;

    /// <summary>The percentage typed for the line, or null when its grams were: whichever was typed is what stays put.</summary>
    public decimal? AuthoredBakersPercent { get; } = authoredBakersPercent;

    /// <summary>What this line adds to the batch's cost, at the ingredient's buffered cost per gram.</summary>
    public decimal Cost => Grams * Ingredient.BufferedCostPerBaseUnit;
}

/// <summary>
/// One sub-assembly in the product's recipe, saved or not: a section of the Recipe Structure, with its
/// ingredient lines scaled to one piece, and a box in the Unit Conversion Overview (a batch in pieces).
/// Either the product's own (it edits the lines), or another product's that it links (read-only lines,
/// its own piece size). Its batch totals come from its lines, so they follow its ingredients' costs.
/// </summary>
public sealed class ProductUsageEdit
{
    /// <summary>Sections show in role order (SubAssemblyRole.SortOrder), then by name.</summary>
    public static IComparer<ProductUsageEdit> RecipeOrder { get; } = Comparer<ProductUsageEdit>.Create((a, b) =>
    {
        var byRole = (a.Role?.SortOrder ?? int.MaxValue).CompareTo(b.Role?.SortOrder ?? int.MaxValue);
        if (byRole == 0)
        {
            byRole = string.Compare(a.RoleName, b.RoleName, StringComparison.CurrentCultureIgnoreCase);
        }

        return byRole != 0 ? byRole : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
    });

    /// <param name="subAssemblyId">The saved sub-assembly's id, or 0 for one of the product's own not saved yet.</param>
    /// <param name="ownerName">Null for the product's own sub-assembly; otherwise the product it's linked from.</param>
    /// <param name="linkedBy">For the product's own sub-assembly, the other products linking it.</param>
    /// <param name="addIns">This product's own extra ingredients on top of a linked recipe; always empty for its own sub-assembly.</param>
    public ProductUsageEdit(
        int subAssemblyId,
        string name,
        SubAssemblyRole? role,
        IReadOnlyList<SubAssemblyLineEdit> lines,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        string? ownerName,
        IReadOnlyList<string> linkedBy,
        IReadOnlyList<SubAssemblyLineEdit>? addIns = null)
    {
        SubAssemblyId = subAssemblyId;
        Name = name;
        Role = role;
        Lines = lines;
        AuthoredField = authoredField;
        AuthoredValue = authoredValue;
        OwnerName = ownerName;
        LinkedBy = linkedBy;
        AddIns = addIns ?? [];

        // The sub-assembly's own batch, then this product's on top of it: add-ins go into the batch, so a
        // piece takes its weight-per-piece share of the two together.
        (BaseTotalWeight, var baseCost) = SubAssemblyRules.CalculateTotals([.. lines.Select(l => (l.Grams, l.Ingredient.BufferedCostPerBaseUnit))]);
        (TotalWeight, TotalCost) = ProductRules.CalculateEffectiveBatch(
            BaseTotalWeight, baseCost, [.. AddIns.Select(a => (a.Grams, a.Ingredient.BufferedCostPerBaseUnit))]);
        (WeightPerPiece, Yield) = ProductRules.ResolvePieceSize(authoredField, authoredValue, TotalWeight);

        // A batch that weighs nothing has nothing to share out per piece.
        if (TotalWeight > 0 && WeightPerPiece > 0)
        {
            CostPerPiece = ProductRules.CalculateUsageCostPerPiece(TotalCost, WeightPerPiece, TotalWeight);
        }
    }

    public int SubAssemblyId { get; }

    public string Name { get; }

    public SubAssemblyRole? Role { get; }

    public int RoleId => Role?.Id ?? 0;

    public string RoleName => Role?.Name ?? string.Empty;

    public IReadOnlyList<SubAssemblyLineEdit> Lines { get; }

    /// <summary>
    /// This product's own extra ingredients on top of a recipe it links — a colouring, a flavouring, an
    /// inclusion. Empty for a sub-assembly of the product's own, whose lines it edits directly.
    /// </summary>
    public IReadOnlyList<SubAssemblyLineEdit> AddIns { get; }

    /// <summary>Which figure the user typed; it stays put when the batch changes, and the other follows.</summary>
    public UsageAuthoredField AuthoredField { get; }

    public decimal AuthoredValue { get; }

    /// <summary>Where a standalone sub-assembly (no owner product) is kept, in place of an owner's name.</summary>
    public const string StandaloneSourceName = "Sub-Assemblies";

    /// <summary>Where a linked sub-assembly comes from: its owner product, or the Sub-Assemblies screen for a standalone one.</summary>
    public static string SourceNameOf(SubAssembly subAssembly) =>
        subAssembly.OwnerProductId is null ? StandaloneSourceName : subAssembly.OwnerProduct?.Name ?? "another product";

    /// <summary>Where it's linked from (another product, or Sub-Assemblies for a standalone one), or null for the product's own.</summary>
    public string? OwnerName { get; }

    public bool IsOwned => OwnerName is null;

    public bool IsLinked => OwnerName is not null;

    /// <summary>The other products linking it: they follow every change to its lines.</summary>
    public IReadOnlyList<string> LinkedBy { get; }

    public bool IsShared => IsOwned && LinkedBy.Count > 0;

    public string LinkedFromText => $"From {OwnerName}";

    public string SharedText => $"Also used by {string.Join(", ", LinkedBy)}";

    /// <summary>The sub-assembly's own batch, before this product's add-ins: what the form's caption compares against.</summary>
    public decimal BaseTotalWeight { get; }

    /// <summary>The batch a piece is cut from: the sub-assembly's own plus this product's add-ins.</summary>
    public decimal TotalWeight { get; }

    public decimal TotalCost { get; }

    public decimal WeightPerPiece { get; }

    /// <summary>Pieces one batch makes. Expected to differ across a product's sub-assemblies.</summary>
    public decimal Yield { get; }

    public bool IsYieldAuthored => AuthoredField == UsageAuthoredField.Yield;

    public bool IsWeightAuthored => AuthoredField == UsageAuthoredField.WeightPerPiece;

    public decimal CostPerPiece { get; }

    /// <summary>E.g. "1,700 g batch".</summary>
    public string BatchText => $"{TotalWeight:#,0.##} g batch";

    /// <summary>E.g. "28.33 pcs".</summary>
    public string YieldText => $"{Yield:#,0.##} pcs";

    /// <summary>E.g. "60 g per piece".</summary>
    public string WeightText => $"{WeightPerPiece:#,0.##} g per piece";

    /// <summary>For the Unit Conversion Overview (Controls/UnitConversionOverview): one batch of the sub-assembly...</summary>
    public string UnitName => $"{Name} batch";

    /// <summary>...and the pieces it makes.</summary>
    public string BaseEquivalentText => $"{Yield:#,0.##} pc";

    /// <summary>The same section with other lines (repriced ingredients, or the owner's edits), add-ins or role.</summary>
    public ProductUsageEdit With(
        SubAssemblyRole? role, IReadOnlyList<SubAssemblyLineEdit> lines, IReadOnlyList<string> linkedBy, IReadOnlyList<SubAssemblyLineEdit>? addIns = null) =>
        new(SubAssemblyId, Name, role, lines, AuthoredField, AuthoredValue, OwnerName, linkedBy, addIns ?? AddIns);

    /// <summary>The same linked section at another piece size, with the add-ins the form now holds.</summary>
    public ProductUsageEdit WithPieceSizeAndAddIns(
        UsageAuthoredField authoredField, decimal authoredValue, IReadOnlyList<SubAssemblyLineEdit> addIns) =>
        new(SubAssemblyId, Name, Role, Lines, authoredField, authoredValue, OwnerName, LinkedBy, addIns);

    /// <summary>Whether <paramref name="other"/> shows the same thing: the same name, role, owner, sharing, piece size, and lines and add-ins at the same costs.</summary>
    public bool ShowsSameAs(ProductUsageEdit other) =>
        SubAssemblyId == other.SubAssemblyId
        && Name == other.Name
        && CollectionMerge.HasSameValues(Role, other.Role)
        && OwnerName == other.OwnerName
        && LinkedBy.SequenceEqual(other.LinkedBy)
        && AuthoredField == other.AuthoredField
        && AuthoredValue == other.AuthoredValue
        && Lines.Count == other.Lines.Count
        && Lines.Zip(other.Lines).All(pair =>
            pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent
            && CollectionMerge.HasSameValues(pair.First.Ingredient, pair.Second.Ingredient))
        && AddIns.Count == other.AddIns.Count
        && AddIns.Zip(other.AddIns).All(pair => pair.First.Grams == pair.Second.Grams && CollectionMerge.HasSameValues(pair.First.Ingredient, pair.Second.Ingredient));
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

/// <summary>Where the Add Sub-Assembly form's sub-assembly comes from.</summary>
public enum UsageFormMode
{
    /// <summary>A new sub-assembly of the product's own.</summary>
    New,

    /// <summary>An existing sub-assembly (another product's, or a standalone one), used as it is and following its changes.</summary>
    Link,

    /// <summary>A copy of an existing sub-assembly (another product's, or a standalone one), the product's own from then on.</summary>
    Copy,
}

public sealed record UsageFormModeOption(string Name, UsageFormMode Mode)
{
    public static IReadOnlyList<UsageFormModeOption> All { get; } =
    [
        new("New sub-assembly", UsageFormMode.New),
        new("Link an existing one", UsageFormMode.Link),
        new("Copy an existing one", UsageFormMode.Copy),
    ];
}

/// <summary>
/// A sub-assembly in the Link and Copy picker: "Sweet Dough — Cream Loaf (Dough)" for another product's,
/// "Chocolate Glaze — Sub-Assemblies (Glaze)" for a standalone one.
/// </summary>
public sealed class LinkableSubAssemblyOption(SubAssembly subAssembly)
{
    public SubAssembly SubAssembly { get; } = subAssembly;

    public string DisplayName => $"{SubAssembly.Name} — {ProductUsageEdit.SourceNameOf(SubAssembly)} ({SubAssembly.Role?.Name})";
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
        decimal laborPercent,
        decimal overheadPercent,
        decimal administrativePercent,
        decimal marginPercent,
        decimal vatPercent)
    {
        SubAssemblies = usageCostsPerPiece.Sum();
        Packaging = packagingCostsPerPiece.Sum();
        Material = ProductRules.CalculateMaterialCostPerPiece(usageCostsPerPiece, packagingCostsPerPiece);
        LaborPercent = laborPercent;
        OverheadPercent = overheadPercent;
        AdministrativePercent = administrativePercent;
        Labor = CostingRules.CalculateCostFromMaterialPercent(Material, laborPercent);
        Overhead = CostingRules.CalculateCostFromMaterialPercent(Material, overheadPercent);
        Administrative = CostingRules.CalculateCostFromMaterialPercent(Material, administrativePercent);
        MarginPercent = marginPercent;
        VatPercent = vatPercent;
        (BaseCost, PreVatPrice, SellingPrice) = CostingRules.CalculateSellingPrice(
            Material, laborPercent, overheadPercent, administrativePercent, marginPercent, vatPercent);
    }

    public decimal SubAssemblies { get; }

    public decimal Packaging { get; }

    public decimal Material { get; }

    public decimal LaborPercent { get; }

    public decimal OverheadPercent { get; }

    public decimal AdministrativePercent { get; }

    /// <summary>The labor cost per piece, in pesos: LaborPercent of the material cost.</summary>
    public decimal Labor { get; }

    /// <summary>The overhead cost per piece, in pesos: OverheadPercent of the material cost.</summary>
    public decimal Overhead { get; }

    /// <summary>The administrative cost per piece, in pesos: AdministrativePercent of the material cost.</summary>
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
/// The Products screen: a product's recipe (its own sub-assemblies and those it links, each at its own
/// piece size, and its packaging), its costing, its packaging link and cloning. The name, description,
/// recipe, packaging and costing are edited in memory, and Save writes them together
/// (IProductRepository.SaveAsync).
/// </summary>
public class ProductsViewModel : ViewModelBase
{
    /// <summary>The details tabs along the bottom: Recipe, Costing, then Links &amp; Clone.</summary>
    public const int RecipeTab = 0;

    public const int LinksTab = 2;

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<string>> NoLinks = new Dictionary<int, IReadOnlyList<string>>();

    private readonly IProductRepository _productRepository;
    private readonly IProductSubAssemblyUsageRepository _usageRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingParametersRepository _costingParametersRepository;
    private readonly IPackagingLinkRepository _packagingLinkRepository;
    private readonly IProductCloner _productCloner;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _subAssemblyLineRepository;
    private readonly IValidator<Product> _productValidator;
    private readonly IValidator<CostingParameters> _costingParametersValidator;
    private readonly IValidator<SubAssembly> _subAssemblyValidator;
    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<Product> _products;
    private readonly ReadOnlyObservableCollection<Product> _visibleProducts;

    /// <summary>Every product, for the packaging link and clone pickers.</summary>
    public ReadOnlyObservableCollection<Product> Products => _products;

    /// <summary>The products the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<Product> VisibleProducts => _visibleProducts;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>The ingredients only, for a sub-assembly's lines: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The packaging materials only: ingredients never go on a packaging line.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>Every sub-assembly, saved or not, its own and linked, in recipe order (ProductUsageEdit.RecipeOrder).</summary>
    public ObservableCollection<ProductUsageEdit> UsageEdits { get; } = [];

    /// <summary>
    /// The Recipe Structure's tabs: a sub-assembly's for each of UsageEdits, at the same index (MirrorUsageTabs),
    /// then AddSubAssemblyTab and PackagingTab.
    /// </summary>
    public ObservableCollection<RecipeTab> RecipeTabs { get; } = [];

    public AddSubAssemblyRecipeTab AddSubAssemblyTab { get; } = new();

    public PackagingRecipeTab PackagingTab { get; } = new();

    private RecipeTab? _selectedRecipeTab;
    /// <summary>The Recipe Structure tab showing. Showing Add Sub-Assembly opens the sub-assembly form.</summary>
    public RecipeTab? SelectedRecipeTab
    {
        get => _selectedRecipeTab;
        set => this.RaiseAndSetIfChanged(ref _selectedRecipeTab, value);
    }

    /// <summary>The last tab other than Add Sub-Assembly that showed, for cancelling an add.</summary>
    private RecipeTab? _lastRecipeContentTab;

    /// <summary>The tab whose form is open: the one a change of tab is leaving, whose form is applied on the way out.</summary>
    private RecipeTab? _shownRecipeTab;

    /// <summary>Set while a refused form puts the tab showing back, so the tab it puts back doesn't apply its own form again.</summary>
    private bool _revertingRecipeTab;

    /// <summary>
    /// Set while the sections are rebuilt. The tab strip picks a neighbour when the tab showing is removed;
    /// that pick is ignored (Add Sub-Assembly would open the form), and ReselectRecipeTab picks one after.
    /// </summary>
    private bool _arrangingRecipeTabs;

    public ObservableCollection<PackagingLineEdit> LineEdits { get; } = [];

    /// <summary>The products this one's packaging can be linked to: every other product.</summary>
    public ObservableCollection<Product> LinkTargetProductOptions { get; } = [];

    /// <summary>Each sub-assembly's, and the packaging's, share of the material cost per piece: the Recipe Cost Overview.</summary>
    public ObservableCollection<CostShareRow> CostShares { get; } = [];

    /// <summary>The picked packaging material's units, for the packaging form's unit picker.</summary>
    public ObservableCollection<RawMaterialUnitOption> EntryUnitOptions { get; } = [];

    public IReadOnlyList<PackagingEntryOption> EntryOptions => PackagingEntryOption.All;

    public IReadOnlyList<UsageFormModeOption> UsageFormModes => UsageFormModeOption.All;

    /// <summary>
    /// Other products' sub-assemblies the form can link or copy. Linking leaves out the ones the recipe
    /// already uses; a product never links or copies its own.
    /// </summary>
    public ObservableCollection<LinkableSubAssemblyOption> LinkableSubAssemblies { get; } = [];

    /// <summary>The sub-assembly form's lines, for a sub-assembly of the product's own: typed straight into its table.</summary>
    public ObservableCollection<IngredientLineRow> FormLines { get; } = [];

    /// <summary>The line the form table's add row edits: it prices itself as its ingredient is picked, before it's committed.</summary>
    public Func<IngredientLineRow> NewFormLine => () => new IngredientLineRow(id => Ingredients.FirstOrDefault(m => m.Id == id), _formFormulaBase);

    /// <summary>
    /// The form's add-ins, for a sub-assembly the product links: its own extras on top of the owner's
    /// recipe, typed straight into their own table under it.
    /// </summary>
    public ObservableCollection<IngredientLineRow> FormAddIns { get; } = [];

    /// <summary>The add-in the form table's add row edits, priced as its ingredient is picked.</summary>
    public Func<IngredientLineRow> NewFormAddIn => () => new IngredientLineRow(id => Ingredients.FirstOrDefault(m => m.Id == id), _formFormulaBase);

    /// <summary>The form's formula base as last worked out, for a line started in either table's add row.</summary>
    private decimal _formFormulaBase;

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
    /// <summary>The header's title, and the usage example's: the product's name, or "New product".</summary>
    public string HeaderTitle => _headerTitle.Value;

    private readonly ObservableAsPropertyHelper<string> _headerInitial;
    /// <summary>The header tile's letter: the title's first.</summary>
    public string HeaderInitial => _headerInitial.Value;

    private readonly ObservableAsPropertyHelper<string> _descriptionLinkText;
    /// <summary>The link under the header's title: the description, or "Add description" while there's none.</summary>
    public string DescriptionLinkText => _descriptionLinkText.Value;

    private bool _isDescriptionEditorOpen;
    /// <summary>
    /// The description's pop-up is open. It edits EditingDescription as it's typed; closing it any way but
    /// Cancel keeps what was typed (EditDescriptionCommand remembers what to go back to).
    /// </summary>
    public bool IsDescriptionEditorOpen
    {
        get => _isDescriptionEditorOpen;
        set => this.RaiseAndSetIfChanged(ref _isDescriptionEditorOpen, value);
    }

    /// <summary>The description when its pop-up opened, for Cancel.</summary>
    private string _descriptionBeforeEdit = string.Empty;

    /// <summary>When the product was last saved, e.g. "Sep 14, 2026, 6:27 PM", or "—".</summary>
    private string _lastUpdatedText = "—";
    public string LastUpdatedText
    {
        get => _lastUpdatedText;
        private set => this.RaiseAndSetIfChanged(ref _lastUpdatedText, value);
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
    /// <summary>"Linked to Cream Loaf", or empty while the product has its own packaging.</summary>
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

    // The add/edit sub-assembly form: the section it edits (null when it adds one), and its fields.
    private ProductUsageEdit? _editingUsage;

    /// <summary>
    /// The section the form edits, or null while it adds one. A sub-assembly's tab is its form, so this is the
    /// tab's own section whenever one shows; the form's header reads its name, badges and where it's linked from.
    /// </summary>
    public ProductUsageEdit? EditingUsage
    {
        get => _editingUsage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _editingUsage, value);
            this.RaisePropertyChanged(nameof(IsEditingLinkedUsage));
        }
    }

    /// <summary>
    /// The form edits a section linked from elsewhere, so Make a Copy has something to copy. A plain
    /// <c>EditingUsage.IsLinked</c> binding can't say this: with no section it has no value at all, and the
    /// button would fall back to showing.
    /// </summary>
    public bool IsEditingLinkedUsage => EditingUsage?.IsLinked == true;

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

    /// <summary>Adding, not editing: only then can the form's sub-assembly be an existing one, linked or copied.</summary>
    private bool _isAddingUsage = true;
    public bool IsAddingUsage
    {
        get => _isAddingUsage;
        private set => this.RaiseAndSetIfChanged(ref _isAddingUsage, value);
    }

    private UsageFormModeOption _editingUsageMode = UsageFormModeOption.All[0];
    public UsageFormModeOption EditingUsageMode
    {
        get => _editingUsageMode;
        set => this.RaiseAndSetIfChanged(ref _editingUsageMode, value);
    }

    private bool _isFormEditingLines = true;
    /// <summary>The form edits a sub-assembly of the product's own: its name and role show, and its lines are typed into.</summary>
    public bool IsFormEditingLines
    {
        get => _isFormEditingLines;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isFormEditingLines, value);
            this.RaisePropertyChanged(nameof(IsFormShowingLines));
            this.RaisePropertyChanged(nameof(IsFormShowingAddIns));
        }
    }

    /// <summary>
    /// The form's lines table shows: the product's own lines, to type into, or a linked one's, read-only. A link
    /// has none to show until the sub-assembly it comes from has been picked and its lines loaded.
    /// </summary>
    public bool IsFormShowingLines => IsFormEditingLines || FormLines.Count > 0;

    /// <summary>
    /// The form's add-ins table shows: only for a sub-assembly the product links, and only once the recipe
    /// it sits on top of is there to sit on. A sub-assembly of the product's own has no add-ins — its
    /// extras just go in its lines.
    /// </summary>
    public bool IsFormShowingAddIns => !IsFormEditingLines && IsFormShowingLines;

    private bool _isFormPickingSource;
    /// <summary>The form links or copies an existing sub-assembly (another product's, or a standalone one): its picker shows.</summary>
    public bool IsFormPickingSource
    {
        get => _isFormPickingSource;
        private set => this.RaiseAndSetIfChanged(ref _isFormPickingSource, value);
    }

    private string _editingUsageName = string.Empty;
    public string EditingUsageName
    {
        get => _editingUsageName;
        set => this.RaiseAndSetIfChanged(ref _editingUsageName, value);
    }

    private SubAssemblyRole? _editingUsageRole;
    public SubAssemblyRole? EditingUsageRole
    {
        get => _editingUsageRole;
        set => this.RaiseAndSetIfChanged(ref _editingUsageRole, value);
    }

    private LinkableSubAssemblyOption? _editingUsageSource;
    /// <summary>The existing sub-assembly to link or copy.</summary>
    public LinkableSubAssemblyOption? EditingUsageSource
    {
        get => _editingUsageSource;
        set => this.RaiseAndSetIfChanged(ref _editingUsageSource, value);
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

    // The piece size: the user types either figure, and the other follows from the batch weight. Only
    // the one typed is kept (_usageAuthoredField); _syncingPieceSize stops each box setting the other back.
    private UsageAuthoredField _usageAuthoredField = UsageAuthoredField.WeightPerPiece;
    private bool _syncingPieceSize;

    private decimal _editingUsageWeightPerPiece;
    public decimal EditingUsageWeightPerPiece
    {
        get => _editingUsageWeightPerPiece;
        set => this.RaiseAndSetIfChanged(ref _editingUsageWeightPerPiece, value);
    }

    private decimal _editingUsageYield;
    public decimal EditingUsageYield
    {
        get => _editingUsageYield;
        set => this.RaiseAndSetIfChanged(ref _editingUsageYield, value);
    }

    private string _usageFormTotalWeightText = "0 g";
    /// <summary>The batch the form's piece size shares out, e.g. "1,700 g".</summary>
    public string UsageFormTotalWeightText
    {
        get => _usageFormTotalWeightText;
        private set => this.RaiseAndSetIfChanged(ref _usageFormTotalWeightText, value);
    }

    private string _usageFormPieceSizeCaption = string.Empty;
    /// <summary>Which figure stays put when the batch changes, e.g. "Weight per piece stays at 60 g if the batch changes".</summary>
    public string UsageFormPieceSizeCaption
    {
        get => _usageFormPieceSizeCaption;
        private set => this.RaiseAndSetIfChanged(ref _usageFormPieceSizeCaption, value);
    }

    private string _usageFormPreviewText = string.Empty;
    /// <summary>What a piece of the form's sub-assembly costs, e.g. "₱0.5000 a piece": the add form's footer.</summary>
    public string UsageFormPreviewText
    {
        get => _usageFormPreviewText;
        private set => this.RaiseAndSetIfChanged(ref _usageFormPreviewText, value);
    }

    private string _usageFormBaseCaption = string.Empty;
    /// <summary>The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by.</summary>
    public string UsageFormBaseCaption
    {
        get => _usageFormBaseCaption;
        private set => this.RaiseAndSetIfChanged(ref _usageFormBaseCaption, value);
    }

    private string _usageFormAddInsCaption = string.Empty;
    /// <summary>The batch the two tables come to, e.g. "1,700 g from Sweet Dough + 20 g of add-ins = a 1,720 g batch."</summary>
    public string UsageFormAddInsCaption
    {
        get => _usageFormAddInsCaption;
        private set => this.RaiseAndSetIfChanged(ref _usageFormAddInsCaption, value);
    }

    private string _usageFormCostPerPieceText = "—";
    /// <summary>The same figure on its own, e.g. "₱0.5000": the form header's, for a section being edited.</summary>
    public string UsageFormCostPerPieceText
    {
        get => _usageFormCostPerPieceText;
        private set => this.RaiseAndSetIfChanged(ref _usageFormCostPerPieceText, value);
    }

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

    private decimal _editingLaborPercent;
    public decimal EditingLaborPercent
    {
        get => _editingLaborPercent;
        set => this.RaiseAndSetIfChanged(ref _editingLaborPercent, value);
    }

    private decimal _editingOverheadPercent;
    public decimal EditingOverheadPercent
    {
        get => _editingOverheadPercent;
        set => this.RaiseAndSetIfChanged(ref _editingOverheadPercent, value);
    }

    private decimal _editingAdministrativePercent;
    public decimal EditingAdministrativePercent
    {
        get => _editingAdministrativePercent;
        set => this.RaiseAndSetIfChanged(ref _editingAdministrativePercent, value);
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

    private Product? _editingPackagingLinkTarget;
    /// <summary>The product the packaging link card would link this one's packaging to.</summary>
    public Product? EditingPackagingLinkTarget
    {
        get => _editingPackagingLinkTarget;
        set => this.RaiseAndSetIfChanged(ref _editingPackagingLinkTarget, value);
    }

    private string _editingCloneName = string.Empty;
    public string EditingCloneName
    {
        get => _editingCloneName;
        set => this.RaiseAndSetIfChanged(ref _editingCloneName, value);
    }

    /// <summary>Every sub-assembly that could be linked or copied, as last loaded; LinkableSubAssemblies filters it.</summary>
    private IReadOnlyList<SubAssembly> _linkable = [];

    /// <summary>For each of the product's own sub-assemblies that others link, the products linking it.</summary>
    private IReadOnlyDictionary<int, IReadOnlyList<string>> _linkedBy = NoLinks;

    // What the product had when it was loaded, so Save only sends the parts that changed.
    private RecipeDraft _savedRecipe = new([], []);
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedLines = [];
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) _savedCosting;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditDescriptionCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseDescriptionEditorCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelDescriptionEditCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> EditUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> MakeCopyCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> RemoveUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ApplyUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveFormLineCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveFormAddInCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ApplyLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelLineCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateExampleCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> LinkPackagingCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> UnlinkPackagingCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloneCommand { get; }

    public ProductsViewModel(
        IProductRepository productRepository,
        IProductSubAssemblyUsageRepository usageRepository,
        IPackagingLineRepository packagingLineRepository,
        ICostingParametersRepository costingParametersRepository,
        IPackagingLinkRepository packagingLinkRepository,
        IProductCloner productCloner,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository subAssemblyLineRepository,
        ISubAssemblyRoleRepository subAssemblyRoleRepository,
        IRawMaterialRepository rawMaterialRepository,
        IValidator<Product> productValidator,
        IValidator<CostingParameters> costingParametersValidator,
        IValidator<SubAssembly> subAssemblyValidator,
        ILogger logger)
        : base(logger)
    {
        _productRepository = productRepository;
        _usageRepository = usageRepository;
        _packagingLineRepository = packagingLineRepository;
        _costingParametersRepository = costingParametersRepository;
        _packagingLinkRepository = packagingLinkRepository;
        _productCloner = productCloner;
        _subAssemblyRepository = subAssemblyRepository;
        _subAssemblyLineRepository = subAssemblyLineRepository;
        _productValidator = productValidator;
        _costingParametersValidator = costingParametersValidator;
        _subAssemblyValidator = subAssemblyValidator;

        RecipeTabs.Add(AddSubAssemblyTab);
        RecipeTabs.Add(PackagingTab);
        UsageEdits.CollectionChanged += (_, e) => MirrorUsageTabs(e);

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

        _descriptionLinkText = this.WhenAnyValue(x => x.EditingDescription)
            .Select(description => string.IsNullOrWhiteSpace(description) ? "Add description" : description.Trim())
            .ToProperty(this, x => x.DescriptionLinkText, "Add description");

        // A hidden tab can't stay the one showing: leaving a saved product while on Links & Clone goes
        // back to Recipe. Subscribed before ShowLinks, so the tab strip moves before the tab hides; a
        // TabControlExt whose selected tab hides picks a neighbour itself.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is null && SelectedDetailsTab == LinksTab)
            .Subscribe(_ => SelectedDetailsTab = RecipeTab);

        _showLinks = this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => product is not null)
            .ToProperty(this, x => x.ShowLinks);

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

        this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Subscribe(RecipeTabShown);

        // The sub-assembly form: where its sub-assembly comes from, then the one it links or copies.
        this.WhenAnyValue(x => x.EditingUsageMode)
            .Skip(1)
            .Where(_ => !IsRefreshing && IsUsageFormOpen && IsAddingUsage)
            .Subscribe(_ => StartUsageFormMode());

        // A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.
        FormLines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .AutoRefresh(l => l.IsFormulaBase)
            .Subscribe(_ =>
            {
                IngredientLineRow.Resolve(FormLines, Ingredients.ToDictionary(m => m.Id));
                FormLinesChanged();
            });

        // An add-in picked, typed or deleted: it's priced, and the batch it joins follows.
        FormAddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ =>
            {
                IngredientLineRow.Resolve(FormAddIns, Ingredients.ToDictionary(m => m.Id));
                FormLinesChanged();
            });

        this.WhenAnyValue(x => x.EditingUsageSource)
            .Skip(1)
            .Where(_ => !IsRefreshing && IsUsageFormOpen && IsAddingUsage)
            .Select(source => Observable.FromAsync(cancellationToken => LoadUsageSourceAsync(source, cancellationToken)))
            .Switch()
            .Subscribe();

        // Typing one figure of the piece size works out the other, and makes it the one that's kept.
        this.WhenAnyValue(x => x.EditingUsageWeightPerPiece)
            .Skip(1)
            .Where(_ => !_syncingPieceSize && !IsRefreshing)
            .Subscribe(_ => PieceSizeTyped(UsageAuthoredField.WeightPerPiece));

        this.WhenAnyValue(x => x.EditingUsageYield)
            .Skip(1)
            .Where(_ => !_syncingPieceSize && !IsRefreshing)
            .Subscribe(_ => PieceSizeTyped(UsageAuthoredField.Yield));

        this.WhenAnyValue(x => x.EditingLinePackagingMaterial)
            .Where(_ => !IsRefreshing)
            .Subscribe(material => ReplaceEntryUnitOptions(material, keepUnitId: null));

        this.WhenAnyValue(
                x => x.EditingLaborPercent, x => x.EditingOverheadPercent, x => x.EditingAdministrativePercent, x => x.EditingMarginPercent, x => x.EditingVatPercent)
            .Subscribe(_ => RecomputeCosts());

        // A worked-out example belongs to the quantity it was worked out for.
        this.WhenAnyValue(x => x.ExampleQuantity)
            .Subscribe(_ => ExampleResult = null);

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, pickers and unsaved edits while
        // picking up what changed elsewhere: a repriced ingredient, or a linked sub-assembly its owner
        // changed.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await subAssemblyRoleRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await rawMaterialRepository.GetAllAsync(CancellationToken.None);
            var products = await _productRepository.GetAllAsync(CancellationToken.None);
            var linkable = await _subAssemblyRepository.GetLinkableAsync(CancellationToken.None);

            // A linked section reads another product's sub-assembly, which can change there (its lines,
            // name or role), so those are fetched again; the product's own sections only need their
            // ingredients' new costs. Who links the product's own sub-assemblies can change too.
            var detailProductId = _detailProductId;
            HashSet<int> linkedIds = [.. UsageEdits.Where(u => u.IsLinked).Select(u => u.SubAssemblyId)];
            var linkedSubAssemblies = await _subAssemblyRepository.GetByIdsAsync(linkedIds, CancellationToken.None);
            var linkedLines = await _subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, CancellationToken.None);
            var linkedBy = detailProductId is { } ownerId
                ? await _subAssemblyRepository.GetLinkedByAsync(ownerId, CancellationToken.None)
                : NoLinks;
            var packagingLink = detailProductId is { } linkProductId
                ? await _packagingLinkRepository.GetForProductAsync(linkProductId, CancellationToken.None)
                : null;

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedProduct?.Id;
            var usageRoleId = EditingUsageRole?.Id;
            var usageSourceId = EditingUsageSource?.SubAssembly.Id;
            var linePackagingMaterialId = EditingLinePackagingMaterial?.Id;
            var lineUnitId = EditingLineUnit?.UnitId;
            var packagingLinkTargetId = EditingPackagingLinkTarget?.Id;

            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
                _productCache.MergeFrom(products);
                _linkable = linkable;

                SelectedProduct = Products.FirstOrDefault(p => p.Id == selectedId);
                if (SelectedProduct is not null)
                {
                    ShowUpdatedAt(SelectedProduct.UpdatedAt);
                }

                if (detailProductId == _detailProductId)
                {
                    _linkedBy = linkedBy;
                    SetPackagingLink(packagingLink);
                }

                RefreshRecipeRows(linkedSubAssemblies, linkedLines);
                RefreshFormLines();
                RefreshLinkableOptions();

                // An add form opened before the roles were loaded (a new product's, shown at start) takes the first role, as it would have.
                EditingUsageRole = Roles.FirstOrDefault(r => r.Id == usageRoleId)
                    ?? (usageRoleId is null && IsUsageFormOpen && IsAddingUsage ? Roles.FirstOrDefault() : null);
                EditingUsageSource = LinkableSubAssemblies.FirstOrDefault(o => o.SubAssembly.Id == usageSourceId);

                var previousLineMaterial = EditingLinePackagingMaterial;
                EditingLinePackagingMaterial = PackagingMaterials.FirstOrDefault(m => m.Id == linePackagingMaterialId);
                if (!ReferenceEquals(previousLineMaterial, EditingLinePackagingMaterial))
                {
                    ReplaceEntryUnitOptions(EditingLinePackagingMaterial, lineUnitId);
                }

                RefreshLinkTargetProductOptions();
                EditingPackagingLinkTarget = LinkTargetProductOptions.FirstOrDefault(p => p.Id == packagingLinkTargetId);

                RecomputeUsageForm();
                RecomputeRecipe();
            });
        });

        NewCommand = ReactiveCommand.Create(StartNewProduct);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            // A form left open is part of what's being saved, unless it's an add form nothing was entered in:
            // the Add Sub-Assembly tab opens one by itself.
            if (IsUsageFormOpen && !IsUsageFormUntouched())
            {
                ApplyUsageForm();
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
                LaborPercent = EditingLaborPercent,
                OverheadPercent = EditingOverheadPercent,
                AdministrativePercent = EditingAdministrativePercent,
                MarginPercent = EditingMarginPercent,
                VatPercent = EditingVatPercent,
            };

            var costingValidation = _costingParametersValidator.Validate(costing);
            if (!costingValidation.IsValid)
            {
                ErrorMessage = string.Join(" ", costingValidation.Errors.Select(e => e.ErrorMessage));
                return;
            }

            var recipe = CurrentRecipe();
            var id = await _productRepository.SaveAsync(
                product,
                IsSameRecipe(recipe, _savedRecipe) ? null : recipe,
                ChangedLines(),
                CurrentCosting() == _savedCosting ? null : costing,
                CancellationToken.None);

            // The list picks up the new name; then the form reloads what was saved, which is what the
            // next Save compares against.
            await LoadCommand.Execute();
            await ReloadSavedProductAsync(id);
        }));

        // The description is edited in a pop-up under the header's title, straight into the form; Save saves it.
        EditDescriptionCommand = ReactiveCommand.Create(() =>
        {
            _descriptionBeforeEdit = EditingDescription;
            IsDescriptionEditorOpen = true;
        });

        CloseDescriptionEditorCommand = ReactiveCommand.Create(() => { IsDescriptionEditorOpen = false; });

        CancelDescriptionEditCommand = ReactiveCommand.Create(() =>
        {
            EditingDescription = _descriptionBeforeEdit;
            IsDescriptionEditorOpen = false;
        });

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync(
                $"Delete '{SelectedProduct!.Name}'? Its own sub-assemblies are deleted with it. This can't be undone.",
                async () =>
                {
                    await _productRepository.DeleteAsync(SelectedProduct!.Id, CancellationToken.None);
                    StartNewProduct();
                    await LoadCommand.Execute();
                }),
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        AddUsageCommand = ReactiveCommand.Create(() => SelectRecipeTab(AddSubAssemblyTab));

        // A sub-assembly's tab is its form, so editing one is just showing its tab (RecipeTabShown opens the form).
        EditUsageCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => SelectRecipeTab(TabFor(usage)));

        // A linked sub-assembly becomes the product's own copy, at the same piece size; saved with Save.
        MakeCopyCommand = ReactiveCommand.Create<ProductUsageEdit>(usage =>
        {
            var ownNames = UsageEdits.Where(u => u.IsOwned).Select(u => u.Name).ToList();

            // Its add-ins become plain lines of the copy: the batch it was showing is the batch it keeps.
            // Never a duplicate ingredient, since an add-in can't be one the recipe already has.
            var copy = new ProductUsageEdit(
                0, SubAssemblyRules.UniqueName(usage.Name, ownNames), usage.Role, [.. usage.Lines, .. usage.AddIns],
                usage.AuthoredField, usage.AuthoredValue, ownerName: null, linkedBy: []);
            ReplaceUsage(usage, copy);
        });

        // Nothing to confirm: the sub-assembly only leaves the recipe, and goes when the product is saved.
        // One that other products link can't go at all; they have to copy or remove it first.
        RemoveUsageCommand = ReactiveCommand.CreateFromTask<ProductUsageEdit>(usage => RunSafelyAsync(() =>
        {
            if (usage.IsShared)
            {
                SubAssemblyRules.EnsureNotLinkedElsewhere(usage.Name, usage.LinkedBy);
            }

            if (ReferenceEquals(usage, EditingUsage))
            {
                CloseUsageForm();
            }

            // Its neighbour's tab shows instead: the next sub-assembly's, else the one before, else Add Sub-Assembly.
            if (SelectedRecipeTab is SubAssemblyRecipeTab showing && ReferenceEquals(showing.Usage, usage))
            {
                var index = UsageEdits.IndexOf(usage);
                var neighbour = index + 1 < UsageEdits.Count ? UsageEdits[index + 1] : index > 0 ? UsageEdits[index - 1] : null;
                SelectRecipeTab(neighbour is null ? AddSubAssemblyTab : TabFor(neighbour));
            }

            UsageEdits.Remove(usage);
            RefreshLinkableOptions();
            RecomputeRecipe();
            return Task.CompletedTask;
        }));

        // Apply puts the form's sub-assembly in the recipe, then shows its tab, where its form stays open.
        ApplyUsageCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(() =>
        {
            SelectRecipeTab(TabFor(ApplyUsageForm()));
            return Task.CompletedTask;
        }));

        CancelUsageCommand = ReactiveCommand.Create(() =>
        {
            // A section's tab has nothing else to show, so Cancel puts back what was last applied to it.
            if (EditingUsage is { } editing)
            {
                OpenUsageForm(editing);
                return;
            }

            CloseUsageForm();

            // Cancelling an add goes back to the tab that showed before it.
            if (SelectedRecipeTab is AddSubAssemblyRecipeTab)
            {
                SelectRecipeTab(_lastRecipeContentTab is { } last && RecipeTabs.Contains(last)
                    ? last
                    : RecipeTabs.OfType<SubAssemblyRecipeTab>().FirstOrDefault() ?? (RecipeTab)PackagingTab);
            }
        });

        RemoveFormLineCommand = ReactiveCommand.Create(
            () => { FormLines.Remove(SelectedFormLine!); },
            this.WhenAnyValue(x => x.SelectedFormLine).Select(line => line is not null));

        RemoveFormAddInCommand = ReactiveCommand.Create(
            () => { FormAddIns.Remove(SelectedFormAddIn!); },
            this.WhenAnyValue(x => x.SelectedFormAddIn).Select(line => line is not null));

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

        // Linking and unlinking the packaging are saved at once. Only the packaging reloads, so unsaved
        // edits to the rest of the product stay.
        LinkPackagingCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                await _packagingLinkRepository.SetLinkAsync(SelectedProduct!.Id, EditingPackagingLinkTarget!.Id, CancellationToken.None);
                await ReloadPackagingAsync();
            }),
            this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingPackagingLinkTarget, (product, target) => product is not null && target is not null));

        UnlinkPackagingCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                await _packagingLinkRepository.RemoveLinkAsync(SelectedProduct!.Id, CancellationToken.None);
                await ReloadPackagingAsync();
            }),
            this.WhenAnyValue(x => x.SelectedProduct, x => x.IsPackagingLinked, (product, linked) => product is not null && linked));

        // A clone copies the product as saved: unsaved edits stay with this one.
        CloneCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(async () =>
            {
                var newId = await _productCloner.CloneAsync(SelectedProduct!.Id, EditingCloneName, CancellationToken.None);
                await LoadCommand.Execute();
                SelectedProduct = Products.FirstOrDefault(p => p.Id == newId);
            }),
            this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingCloneName, (product, name) => product is not null && !string.IsNullOrWhiteSpace(name)));

        // A new product starts on Add Sub-Assembly, with its form open.
        SelectRecipeTab(AddSubAssemblyTab);
    }

    /// <summary>The Last Updated figure: the day and the time; "—" for a product never saved since it was added.</summary>
    private void ShowUpdatedAt(DateTime? updatedAt) =>
        LastUpdatedText = updatedAt?.ToString("MMM d, yyyy, h:mm tt") ?? "—";

    private void LoadIntoForm(Product product)
    {
        IsDescriptionEditorOpen = false;
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
        EditingPackagingLinkTarget = null;
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
        IsDescriptionEditorOpen = false;
        ShowUpdatedAt(null);
        EditingCloneName = string.Empty;

        // After an unsaved new product nothing was selected, so the selection subscriptions don't run.
        CloseUsageForm();
        CloseLineForm();
        SelectedLineEdit = null;
        EditingPackagingLinkTarget = null;
        ClearDetails();
    }

    /// <summary>Empties the recipe, packaging, costing and links, and what Save compares them against.</summary>
    private void ClearDetails()
    {
        var previousTab = SelectedRecipeTab;
        _detailProductId = null;
        _linkedBy = NoLinks;

        _arrangingRecipeTabs = true;
        try
        {
            UsageEdits.Clear();
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        LineEdits.Clear();
        SetPackagingLink(null);
        _savedRecipe = new RecipeDraft([], []);
        _savedLines = [];
        SetCosting(null);
        RefreshLinkableOptions();
        RecomputeRecipe();

        // A picked product's sub-assemblies are on their way, and pick the tab when they arrive.
        if (SelectedProduct is null)
        {
            ReselectRecipeTab(previousTab, previousUsage: null);
        }
    }

    private async Task LoadProductDetailAsync(Product? product, CancellationToken cancellationToken)
    {
        if (product is null)
        {
            return;
        }

        var packagingLink = await _packagingLinkRepository.GetForProductAsync(product.Id, cancellationToken);
        var usages = await _usageRepository.GetForProductAsync(product.Id, cancellationToken);
        var lines = await _packagingLineRepository.GetEffectiveForProductAsync(product.Id, cancellationToken);
        var costing = await _costingParametersRepository.GetForProductAsync(product.Id, cancellationToken);
        var subAssemblyLines = await _subAssemblyLineRepository.GetForSubAssembliesAsync(
            [.. usages.Select(u => u.SubAssemblyId).Distinct()], cancellationToken);
        var addIns = await _usageRepository.GetAddInsForProductAsync(product.Id, cancellationToken);
        var linkedBy = await _subAssemblyRepository.GetLinkedByAsync(product.Id, cancellationToken);

        // Only the product picked last is shown; a newer pick cancels this load.
        cancellationToken.ThrowIfCancellationRequested();

        _detailProductId = product.Id;
        _linkedBy = linkedBy;
        SetPackagingLink(packagingLink);
        ReplaceUsages(usages, subAssemblyLines, addIns);
        ReplaceLines(lines);
        SetCosting(costing);
        RefreshLinkableOptions();
        RecomputeRecipe();
    }

    /// <summary>After the packaging was linked or unlinked: the link, and the lines the product now shows, which Save compares against.</summary>
    private async Task ReloadPackagingAsync()
    {
        var productId = SelectedProduct!.Id;
        var link = await _packagingLinkRepository.GetForProductAsync(productId, CancellationToken.None);
        var lines = await _packagingLineRepository.GetEffectiveForProductAsync(productId, CancellationToken.None);

        CloseLineForm();
        SetPackagingLink(link);
        ReplaceLines(lines);
        RecomputeRecipe();
    }

    private void SetPackagingLink(PackagingLink? link)
    {
        IsPackagingLinked = link is not null;
        PackagingLinkedToText = link is null ? string.Empty : $"Linked to {link.LinkedProduct?.Name}";
    }

    /// <summary>The saved usages as sections, in recipe order; they're also what Save compares the recipe against.</summary>
    private void ReplaceUsages(
        IReadOnlyList<ProductSubAssemblyUsage> usages, IReadOnlyList<SubAssemblyLine> subAssemblyLines, IReadOnlyList<UsageAddInLine> addIns)
    {
        var linesBySubAssembly = subAssemblyLines.ToLookup(l => l.SubAssemblyId);

        // Add-ins key on the usage, not the sub-assembly: they're this product's own, so two products
        // linking the same one have different sets.
        var addInsByUsage = addIns.ToLookup(a => a.UsageId);
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var previousTab = SelectedRecipeTab;

        _arrangingRecipeTabs = true;
        try
        {
            UsageEdits.Clear();
            foreach (var usage in usages
                .Select(u => ToUsageEdit(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], ingredientsById))
                .Order(ProductUsageEdit.RecipeOrder))
            {
                UsageEdits.Add(usage);
            }
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        _savedRecipe = CurrentRecipe();
        ReselectRecipeTab(previousTab, (previousTab as SubAssemblyRecipeTab)?.Usage);
    }

    private ProductUsageEdit ToUsageEdit(
        ProductSubAssemblyUsage usage,
        IEnumerable<SubAssemblyLine> lines,
        IEnumerable<UsageAddInLine> addIns,
        IReadOnlyDictionary<int, RawMaterial> ingredientsById)
    {
        var subAssembly = usage.SubAssembly!;
        var isOwn = subAssembly.OwnerProductId == usage.ProductId;
        return new ProductUsageEdit(
            subAssembly.Id,
            subAssembly.Name,
            Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role,
            [.. lines.Select(l => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
            usage.AuthoredField,
            usage.AuthoredValue,
            isOwn ? null : ProductUsageEdit.SourceNameOf(subAssembly),
            isOwn ? _linkedBy.GetValueOrDefault(subAssembly.Id) ?? [] : [],
            [.. addIns.Select(a => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(a.IngredientId) ?? a.Ingredient!, a.Grams))]);
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
        EditingLaborPercent = costing?.LaborPercent ?? 0m;
        EditingOverheadPercent = costing?.OverheadPercent ?? 0m;
        EditingAdministrativePercent = costing?.AdministrativePercent ?? 0m;
        EditingMarginPercent = costing?.MarginPercent ?? 0m;
        EditingVatPercent = costing?.VatPercent ?? 0m;
        _savedCosting = CurrentCosting();
    }

    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) CurrentCosting() =>
        (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent, EditingVatPercent);

    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> CurrentLines() =>
        [.. LineEdits.Select(l => (l.PackagingMaterial.Id, l.Unit.UnitId, l.AuthoredField, l.AuthoredValue))];

    /// <summary>The recipe as Save writes it: the product's own sub-assemblies with their lines, and the ones it links.</summary>
    private RecipeDraft CurrentRecipe() =>
        new(
            [.. UsageEdits.Where(u => u.IsOwned).Select(u => new OwnedSubAssemblyDraft(
                u.SubAssemblyId, u.Name, u.RoleId, [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], u.AuthoredField, u.AuthoredValue))],
            [.. UsageEdits.Where(u => u.IsLinked).Select(u => new LinkedUsageDraft(u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)
            {
                AddIns = [.. u.AddIns.Select(a => (a.Ingredient.Id, a.Grams))],
            })]);

    /// <summary>
    /// Whether two recipes would save the same: the same sub-assemblies, names, roles, lines, add-ins and
    /// piece sizes, in the same order. A linked draft is compared field by field rather than as a record,
    /// as an owned one already is: a record's own Equals compares its AddIns list by reference, which no
    /// two recipes built separately ever share, so Save would write the recipe every time.
    /// </summary>
    private static bool IsSameRecipe(RecipeDraft a, RecipeDraft b) =>
        a.Linked.Count == b.Linked.Count
        && a.Linked.Zip(b.Linked).All(pair =>
            pair.First.SubAssemblyId == pair.Second.SubAssemblyId
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.AddIns.SequenceEqual(pair.Second.AddIns))
        && a.Owned.Count == b.Owned.Count
        && a.Owned.Zip(b.Owned).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.Name == pair.Second.Name
            && pair.First.RoleId == pair.Second.RoleId
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.Lines.SequenceEqual(pair.Second.Lines));

    /// <summary>The packaging lines when they differ from those last loaded, else null. Linked packaging is never saved here.</summary>
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

    /// <summary>The add form, fresh for its mode: a new sub-assembly's name, role and lines, or the pick of another product's.</summary>
    private void StartUsageFormMode()
    {
        var mode = EditingUsageMode.Mode;
        IsFormEditingLines = mode != UsageFormMode.Link;
        IsFormPickingSource = mode != UsageFormMode.New;
        EditingUsageName = string.Empty;
        EditingUsageRole = Roles.FirstOrDefault();
        FormLines.Clear();
        FormAddIns.Clear();
        SelectedFormLine = null;
        SelectedFormAddIn = null;
        RefreshLinkableOptions();
        EditingUsageSource = null;
        SetPieceSize(UsageAuthoredField.WeightPerPiece, 0m);
    }

    /// <summary>
    /// After a sub-assembly was picked to link or copy: its lines, for the batch weight, and for a copy
    /// also its name (made unique within the product) and role, all editable from then on.
    /// </summary>
    private async Task LoadUsageSourceAsync(LinkableSubAssemblyOption? source, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubAssemblyLineEdit> lines = [];
        if (source is not null)
        {
            var saved = await _subAssemblyLineRepository.GetForSubAssembliesAsync([source.SubAssembly.Id], cancellationToken);
            var ingredientsById = Ingredients.ToDictionary(m => m.Id);
            lines = [.. saved.Select(l => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams))];
        }

        cancellationToken.ThrowIfCancellationRequested();

        // The form moved on while the lines loaded (applied, cancelled, or another pick): they're stale.
        if (!IsUsageFormOpen || !IsAddingUsage || !ReferenceEquals(EditingUsageSource, source))
        {
            return;
        }

        ShowFormLines(lines);
        if (EditingUsageMode.Mode == UsageFormMode.Copy && source is not null)
        {
            EditingUsageName = SubAssemblyRules.UniqueName(source.SubAssembly.Name, [.. UsageEdits.Where(u => u.IsOwned).Select(u => u.Name)]);
            EditingUsageRole = Roles.FirstOrDefault(r => r.Id == source.SubAssembly.RoleId) ?? EditingUsageRole;
        }

        FormLinesChanged();
    }

    /// <summary>
    /// Puts the form's sub-assembly in the recipe and closes the form, without moving the tab showing: the caller
    /// decides which tab follows, since Apply shows the sub-assembly's own and a tab change shows the one left for.
    /// An edit that changes nothing is left alone, so merely looking at a section doesn't churn the recipe.
    /// </summary>
    private ProductUsageEdit ApplyUsageForm()
    {
        var authoredValue = CurrentAuthoredValue();
        ProductUsageEdit usage;

        if (EditingUsage is { IsLinked: true } linked)
        {
            EnsurePieceSizeEntered(authoredValue, linked.Name);
            usage = linked.WithPieceSizeAndAddIns(_usageAuthoredField, authoredValue, CompleteAddIns(linked.Name, linked.Lines));
        }
        else if (IsFormEditingLines)
        {
            var name = EditingUsageName.Trim();
            var validation = _subAssemblyValidator.Validate(new SubAssembly { Name = name, RoleId = EditingUsageRole?.Id ?? 0 });
            if (!validation.IsValid)
            {
                throw new InvalidOperationException(string.Join(" ", validation.Errors.Select(e => e.ErrorMessage)));
            }

            var lines = IngredientLineRow.CompleteLines(FormLines, name);
            if (lines.Count == 0)
            {
                throw new InvalidOperationException($"Add the ingredients that go into {name}, in grams.");
            }

            SubAssemblyRules.EnsureValidOwnedNames([.. UsageEdits.Where(u => u.IsOwned && !ReferenceEquals(u, EditingUsage)).Select(u => u.Name), name]);
            EnsurePieceSizeEntered(authoredValue, name);

            usage = new ProductUsageEdit(
                EditingUsage?.SubAssemblyId ?? 0, name, EditingUsageRole, [.. lines.Select(l => new SubAssemblyLineEdit(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], _usageAuthoredField, authoredValue, ownerName: null, EditingUsage?.LinkedBy ?? []);
        }
        else
        {
            var source = EditingUsageSource?.SubAssembly
                ?? throw new InvalidOperationException("Pick the sub-assembly to link, or cancel the form.");
            EnsurePieceSizeEntered(authoredValue, source.Name);

            // The table's read-only lines are the ones LoadUsageSourceAsync loaded for this very pick; a pick
            // whose lines are still on their way has none yet, and linking it would make a batch of nothing.
            if (FormLines.Count == 0)
            {
                throw new InvalidOperationException($"{source.Name} is still loading its ingredients. Try again in a moment.");
            }

            IReadOnlyList<SubAssemblyLineEdit> sourceLines =
                [.. FormLines.Where(l => l.Ingredient is not null).Select(l => new SubAssemblyLineEdit(l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))];

            usage = new ProductUsageEdit(
                source.Id,
                source.Name,
                Roles.FirstOrDefault(r => r.Id == source.RoleId) ?? source.Role,
                sourceLines,
                _usageAuthoredField,
                authoredValue,
                ProductUsageEdit.SourceNameOf(source),
                linkedBy: [],
                CompleteAddIns(source.Name, sourceLines));
        }

        if (EditingUsage is { } editing)
        {
            if (usage.ShowsSameAs(editing))
            {
                CloseUsageForm();
                return editing;
            }

            ReplaceInRecipeOrder(editing, usage);
        }
        else
        {
            InsertInRecipeOrder(usage);
        }

        CloseUsageForm();
        RefreshLinkableOptions();
        RecomputeRecipe();
        return usage;
    }

    /// <summary>
    /// Applies the form of a tab being left, so nothing typed into it is lost (the grids commit the same way when
    /// focus leaves them). False when a rule refused it: the message shows and the caller stays on the tab.
    /// </summary>
    private bool CommitUsageFormOnLeave()
    {
        if (!IsUsageFormOpen)
        {
            return true;
        }

        // An add form nothing was entered in was opened by the Add Sub-Assembly tab, not by the user.
        if (IsUsageFormUntouched())
        {
            CloseUsageForm();
            return true;
        }

        try
        {
            ErrorMessage = null;
            ApplyUsageForm();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// The add-ins table as lines, refused if one is an ingredient <paramref name="baseLines"/> already has.
    /// The repository checks the same rule as it writes them; this is what puts the message on the screen
    /// the moment Apply is pressed, or the tab is left.
    /// </summary>
    private IReadOnlyList<SubAssemblyLineEdit> CompleteAddIns(string subAssemblyName, IReadOnlyList<SubAssemblyLineEdit> baseLines)
    {
        var addIns = IngredientLineRow.CompleteLines(FormAddIns, subAssemblyName);
        ProductRules.EnsureValidAddIns(
            [.. addIns.Select(a => (a.Ingredient.Id, a.Grams, a.Ingredient.Name))],
            subAssemblyName,
            [.. baseLines.Select(l => l.Ingredient.Id)]);

        return [.. addIns.Select(a => new SubAssemblyLineEdit(a.Ingredient, a.Grams))];
    }

    private static void EnsurePieceSizeEntered(decimal authoredValue, string name)
    {
        if (authoredValue <= 0)
        {
            throw new InvalidOperationException($"Enter how many grams of {name} go into one piece, or how many pieces a batch makes.");
        }
    }

    private void CloseUsageForm()
    {
        IsUsageFormOpen = false;
        EditingUsage = null;
    }

    /// <summary>The form on <paramref name="usage"/>, as it was last applied: the whole of its tab.</summary>
    private void OpenUsageForm(ProductUsageEdit usage)
    {
        EditingUsage = usage;
        IsAddingUsage = false;
        UsageFormTitle = usage.IsOwned ? $"Edit {usage.Name}" : $"Edit {usage.Name} (from {usage.OwnerName})";
        IsFormEditingLines = usage.IsOwned;
        IsFormPickingSource = false;
        EditingUsageSource = null;
        EditingUsageName = usage.Name;
        EditingUsageRole = Roles.FirstOrDefault(r => r.Id == usage.RoleId) ?? usage.Role;

        // A linked one's lines show too, read-only: they're its owner's. What is this product's is its piece
        // size and its add-ins, which go in the table under them.
        ShowFormLines(usage.Lines);
        ShowFormAddIns(usage.AddIns);
        SelectedFormLine = null;
        SelectedFormAddIn = null;
        SetPieceSize(usage.AuthoredField, usage.AuthoredValue);
        IsUsageFormOpen = true;
    }

    /// <summary>A fresh form to add a sub-assembly, in place of any the form had open.</summary>
    private void StartAddingUsage()
    {
        EditingUsage = null;
        UsageFormTitle = "Add Sub-Assembly";
        IsAddingUsage = true;
        EditingUsageMode = UsageFormModeOption.All[0];
        IsUsageFormOpen = true;
        StartUsageFormMode();
    }

    /// <summary>The add form as it opened: no name typed, nothing picked to link or copy, no line and no piece size.</summary>
    private bool IsUsageFormUntouched() =>
        IsAddingUsage
        && string.IsNullOrWhiteSpace(EditingUsageName)
        && EditingUsageSource is null
        && FormLines.All(l => l.IngredientId is null && l.Grams == 0)
        && FormAddIns.All(l => l.IngredientId is null && l.Grams == 0)
        && CurrentAuthoredValue() == 0;

    /// <summary>The form's lines changed, or its source's were loaded: the batch weight, the other piece-size figure and the preview follow.</summary>
    private void FormLinesChanged()
    {
        this.RaisePropertyChanged(nameof(IsFormShowingLines));
        this.RaisePropertyChanged(nameof(IsFormShowingAddIns));
        _syncingPieceSize = true;
        try
        {
            UpdateOtherPieceSizeFigure();
        }
        finally
        {
            _syncingPieceSize = false;
        }

        RecomputeUsageForm();
    }

    /// <summary>Shows a piece size in the form, with the other figure worked out, without either counting as typed.</summary>
    private void SetPieceSize(UsageAuthoredField authoredField, decimal authoredValue)
    {
        _usageAuthoredField = authoredField;
        _syncingPieceSize = true;
        try
        {
            if (authoredField == UsageAuthoredField.WeightPerPiece)
            {
                EditingUsageWeightPerPiece = authoredValue;
            }
            else
            {
                EditingUsageYield = authoredValue;
            }

            UpdateOtherPieceSizeFigure();
        }
        finally
        {
            _syncingPieceSize = false;
        }

        RecomputeUsageForm();
    }

    private void PieceSizeTyped(UsageAuthoredField authoredField)
    {
        _usageAuthoredField = authoredField;
        _syncingPieceSize = true;
        try
        {
            UpdateOtherPieceSizeFigure();
        }
        finally
        {
            _syncingPieceSize = false;
        }

        RecomputeUsageForm();
    }

    /// <summary>Works out the figure the user didn't type from the one they did, rounded to the 2 decimals the box shows. Call with _syncingPieceSize set.</summary>
    private void UpdateOtherPieceSizeFigure()
    {
        var (weightPerPiece, yield) = ProductRules.ResolvePieceSize(_usageAuthoredField, CurrentAuthoredValue(), UsageFormBatch().Sum(l => l.Grams));
        if (_usageAuthoredField == UsageAuthoredField.WeightPerPiece)
        {
            EditingUsageYield = Math.Round(yield, 2);
        }
        else
        {
            EditingUsageWeightPerPiece = Math.Round(weightPerPiece, 2);
        }
    }

    private decimal CurrentAuthoredValue() =>
        _usageAuthoredField == UsageAuthoredField.WeightPerPiece ? EditingUsageWeightPerPiece : EditingUsageYield;

    /// <summary>
    /// The form's batch, as each line's grams and buffered cost per gram: both its tables, the recipe and
    /// this product's add-ins on top of it, leaving out any row with no ingredient picked yet. Everything
    /// the form works out from the batch — the total weight, the other piece-size figure, the caption and
    /// the cost preview — reads it here, so add-ins reach all of them at once.
    /// </summary>
    private IReadOnlyList<(decimal Grams, decimal CostPerGram)> UsageFormBatch() =>
        [.. FormLines.Concat(FormAddIns).Where(l => l.Ingredient is not null).Select(l => (l.Grams, l.Ingredient!.BufferedCostPerBaseUnit))];

    /// <summary>The form's batch weight, which figure stays put, and what a piece costs.</summary>
    private void RecomputeUsageForm()
    {
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(UsageFormBatch());
        var authoredValue = CurrentAuthoredValue();
        var (weightPerPiece, _) = ProductRules.ResolvePieceSize(_usageAuthoredField, authoredValue, totalWeight);

        UsageFormTotalWeightText = $"{totalWeight:#,0.##} g";
        UsageFormPieceSizeCaption = authoredValue <= 0
            ? "Type the weight per piece or the yield; the other is worked out."
            : _usageAuthoredField == UsageAuthoredField.WeightPerPiece
                ? $"Weight per piece stays at {authoredValue:#,0.##} g if the batch changes."
                : $"The yield stays at {authoredValue:#,0.##} pieces if the batch changes.";

        var costPerPiece = totalWeight > 0 && weightPerPiece > 0
            ? ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, totalWeight)
            : (decimal?)null;
        UsageFormPreviewText = costPerPiece is { } preview ? $"₱{preview:N4} a piece" : string.Empty;
        UsageFormCostPerPieceText = costPerPiece is { } figure ? $"₱{figure:N4}" : "—";

        // The formula's base, given to both tables: an add-in is read against the recipe's base, never its own.
        var formulaBase = IngredientLineRow.ApplyFormulaBase([.. FormLines]);
        _formFormulaBase = formulaBase;
        IngredientLineRow.ApplyFormulaBase(FormAddIns, formulaBase);
        UsageFormBaseCaption = formulaBase > 0
            ? $"Base {formulaBase:#,0.##} g · whole formula {totalWeight / formulaBase * 100m:#,0.#}%"
            : "Tick the ingredients that make 100% — usually the flour.";

        // The arithmetic of the two tables, in words: the recipe's grams, this product's on top, the batch.
        var recipeName = EditingUsage?.Name ?? EditingUsageSource?.SubAssembly.Name ?? "the recipe";
        var addInWeight = FormAddIns.Where(l => l.Ingredient is not null).Sum(l => l.Grams);
        var baseWeight = totalWeight - addInWeight;
        UsageFormAddInsCaption = addInWeight > 0
            ? $"{baseWeight:#,0.##} g from {recipeName} + {addInWeight:#,0.##} g of add-ins = a {totalWeight:#,0.##} g batch."
            : $"Nothing extra yet — the batch is {recipeName}'s {baseWeight:#,0.##} g.";
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

    private void ReplaceUsage(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var wasEditing = ReferenceEquals(old, EditingUsage);
        if (wasEditing)
        {
            CloseUsageForm();
        }

        ReplaceInRecipeOrder(old, replacement);
        RefreshLinkableOptions();
        RecomputeRecipe();

        // Its tab is its form, so the form opens again on what took its place.
        if (wasEditing)
        {
            OpenUsageForm(replacement);
        }
    }

    /// <summary>
    /// Puts <paramref name="replacement"/> in <paramref name="old"/>'s place, then moves it to where it sorts
    /// (a new name or role can move it). Replacing rather than removing keeps its tab, and the tab showing.
    /// </summary>
    private void ReplaceInRecipeOrder(ProductUsageEdit old, ProductUsageEdit replacement)
    {
        var index = UsageEdits.IndexOf(old);
        if (index < 0)
        {
            InsertInRecipeOrder(replacement);
            return;
        }

        UsageEdits[index] = replacement;
        var sortedIndex = UsageEdits.Count(u => !ReferenceEquals(u, replacement) && ProductUsageEdit.RecipeOrder.Compare(u, replacement) <= 0);
        if (sortedIndex != index)
        {
            UsageEdits.Move(index, sortedIndex);
        }
    }

    /// <summary>
    /// Keeps RecipeTabs' sub-assembly tabs in step with UsageEdits, index for index. A replaced section keeps
    /// its tab, and the form follows it when it was the one being edited (a refresh swaps in a copy with new
    /// costs), so neither a refresh nor an Apply moves the tab showing or loses the form's place.
    /// </summary>
    private void MirrorUsageTabs(NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                for (var i = 0; i < e.NewItems!.Count; i++)
                {
                    RecipeTabs.Insert(e.NewStartingIndex + i, new SubAssemblyRecipeTab((ProductUsageEdit)e.NewItems[i]!));
                }

                break;

            case NotifyCollectionChangedAction.Remove:
                for (var i = 0; i < e.OldItems!.Count; i++)
                {
                    RecipeTabs.RemoveAt(e.OldStartingIndex);
                }

                break;

            case NotifyCollectionChangedAction.Replace:
            {
                var replacement = (ProductUsageEdit)e.NewItems![0]!;
                ((SubAssemblyRecipeTab)RecipeTabs[e.NewStartingIndex]).Usage = replacement;
                if (ReferenceEquals(e.OldItems![0], EditingUsage))
                {
                    EditingUsage = replacement;

                    // A linked one's lines aren't the form's to keep: show the owner's, as they now are.
                    if (replacement.IsLinked)
                    {
                        ShowFormLines(replacement.Lines);
                    }
                }

                break;
            }

            case NotifyCollectionChangedAction.Move:
                RecipeTabs.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;

            default:
                while (RecipeTabs[0] is SubAssemblyRecipeTab)
                {
                    RecipeTabs.RemoveAt(0);
                }

                break;
        }
    }

    private SubAssemblyRecipeTab TabFor(ProductUsageEdit usage) =>
        RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => ReferenceEquals(t.Usage, usage));

    /// <summary>Shows <paramref name="tab"/>, and does what showing it does even when it was already showing.</summary>
    private void SelectRecipeTab(RecipeTab tab)
    {
        SelectedRecipeTab = tab;
        RecipeTabShown(tab);
    }

    /// <summary>
    /// A tab was picked, by the user or the screen. Leaving one applies the form it was showing, so nothing typed
    /// is lost; a rule refusing it keeps the tab it was on, with the message shown. A sub-assembly's tab then
    /// opens its form, which is all it shows, and Add Sub-Assembly opens a form to add one (a form already adding
    /// one is left as it is). So a form still open is always one Save should apply.
    /// </summary>
    private void RecipeTabShown(RecipeTab? tab)
    {
        if (_arrangingRecipeTabs || _revertingRecipeTab)
        {
            return;
        }

        if (!ReferenceEquals(_shownRecipeTab, tab) && !CommitUsageFormOnLeave())
        {
            _revertingRecipeTab = true;
            try
            {
                SelectedRecipeTab = _shownRecipeTab;
            }
            finally
            {
                _revertingRecipeTab = false;
            }

            return;
        }

        _shownRecipeTab = tab;

        switch (tab)
        {
            case SubAssemblyRecipeTab subAssembly:
                _lastRecipeContentTab = tab;
                if (!ReferenceEquals(EditingUsage, subAssembly.Usage))
                {
                    OpenUsageForm(subAssembly.Usage);
                }

                break;

            case AddSubAssemblyRecipeTab:
                if (!IsUsageFormOpen || !IsAddingUsage)
                {
                    StartAddingUsage();
                }

                break;

            default:
                if (tab is not null)
                {
                    _lastRecipeContentTab = tab;
                }

                break;
        }
    }

    /// <summary>
    /// After the sections were rebuilt (a product loaded, reloaded after Save, or cleared for a new one): the
    /// tab that showed if it's still there (the same sub-assembly's, or Packaging), else the first
    /// sub-assembly's, else Add Sub-Assembly.
    /// </summary>
    private void ReselectRecipeTab(RecipeTab? previous, ProductUsageEdit? previousUsage)
    {
        RecipeTab? tab = previous is PackagingRecipeTab ? previous : null;
        if (previousUsage is not null)
        {
            tab = RecipeTabs.OfType<SubAssemblyRecipeTab>().FirstOrDefault(t => IsSameSubAssembly(t.Usage, previousUsage));
        }

        SelectRecipeTab(tab ?? RecipeTabs.OfType<SubAssemblyRecipeTab>().FirstOrDefault() ?? (RecipeTab)AddSubAssemblyTab);
    }

    /// <summary>A saved sub-assembly is known by its id; one of the product's own saved since, by its name.</summary>
    private static bool IsSameSubAssembly(ProductUsageEdit usage, ProductUsageEdit previous) =>
        previous.SubAssemblyId > 0
            ? usage.SubAssemblyId == previous.SubAssemblyId
            : usage.IsOwned && string.Equals(usage.Name, previous.Name, StringComparison.CurrentCultureIgnoreCase);

    /// <summary>
    /// The sub-assemblies the form can link or copy: the standalone ones and every other product's, less
    /// (when linking) those the recipe already uses. A pick that's no longer offered is cleared.
    /// </summary>
    private void RefreshLinkableOptions()
    {
        var linking = EditingUsageMode.Mode == UsageFormMode.Link;
        var usedIds = UsageEdits.Where(u => u.SubAssemblyId > 0).Select(u => u.SubAssemblyId).ToHashSet();

        // A new product has no id yet, and a standalone sub-assembly no owner: that isn't a match.
        LinkableSubAssemblies.MergeFrom(
            [.. _linkable
                .Where(s => (s.OwnerProductId is null || s.OwnerProductId != _detailProductId) && !(linking && usedIds.Contains(s.Id)))
                .Select(s => new LinkableSubAssemblyOption(s))],
            o => o.SubAssembly.Id);

        if (EditingUsageSource is { } picked && !LinkableSubAssemblies.Contains(picked))
        {
            EditingUsageSource = null;
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
    /// Points the recipe's sections, saved or not, at the refreshed ingredients and roles, and a linked
    /// section at its refreshed sub-assembly and lines, keeping each section's own piece size, so costs
    /// changed on other tabs reach the recipe and the cost summary. Only a section that shows something
    /// different is rebuilt. The packaging rows are pointed at their refreshed materials the same way.
    /// </summary>
    private void RefreshRecipeRows(IReadOnlyList<SubAssembly> linkedSubAssemblies, IReadOnlyList<SubAssemblyLine> linkedLines)
    {
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        var linkedById = linkedSubAssemblies.ToDictionary(s => s.Id);
        var linkedLinesById = linkedLines.ToLookup(l => l.SubAssemblyId);

        var changed = false;
        for (var i = 0; i < UsageEdits.Count; i++)
        {
            var edit = UsageEdits[i];
            ProductUsageEdit fresh;
            if (edit.IsLinked)
            {
                if (!linkedById.TryGetValue(edit.SubAssemblyId, out var subAssembly))
                {
                    continue;
                }

                // Its lines come fresh from its owner; its add-ins are this product's own, so they only
                // need their ingredients' new costs.
                fresh = new ProductUsageEdit(
                    subAssembly.Id,
                    subAssembly.Name,
                    Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role,
                    [.. linkedLinesById[subAssembly.Id].Select(l => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    edit.AuthoredField,
                    edit.AuthoredValue,
                    ProductUsageEdit.SourceNameOf(subAssembly),
                    linkedBy: [],
                    Repriced(edit.AddIns, ingredientsById));
            }
            else
            {
                fresh = edit.With(
                    Roles.FirstOrDefault(r => r.Id == edit.RoleId) ?? edit.Role,
                    Repriced(edit.Lines, ingredientsById),
                    edit.SubAssemblyId > 0 ? _linkedBy.GetValueOrDefault(edit.SubAssemblyId) ?? [] : []);
            }

            if (!fresh.ShowsSameAs(edit))
            {
                UsageEdits[i] = fresh;
                changed = true;
            }
        }

        // A renamed role or sub-assembly can move a section.
        if (changed)
        {
            var sorted = UsageEdits.Order(ProductUsageEdit.RecipeOrder).ToList();
            for (var i = 0; i < sorted.Count; i++)
            {
                var from = UsageEdits.IndexOf(sorted[i]);
                if (from != i)
                {
                    UsageEdits.Move(from, i);
                }
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

    /// <summary>
    /// The form's lines and add-ins at their ingredients' refreshed costs. It reprices in place rather than
    /// refilling the tables, so a refresh arriving while an add-in is being typed doesn't throw it away.
    /// </summary>
    private void RefreshFormLines()
    {
        var ingredientsById = Ingredients.ToDictionary(m => m.Id);
        IngredientLineRow.Resolve(FormLines, ingredientsById);
        IngredientLineRow.Resolve(FormAddIns, ingredientsById);
    }

    /// <summary>Puts <paramref name="lines"/> in the form's table, in place of whatever it held.</summary>
    private void ShowFormLines(IEnumerable<SubAssemblyLineEdit> lines)
    {
        FormLines.Clear();
        FormLines.AddRange(lines.Select(l => new IngredientLineRow(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)));
    }

    /// <summary>The same for the add-ins table under it.</summary>
    private void ShowFormAddIns(IEnumerable<SubAssemblyLineEdit> addIns)
    {
        FormAddIns.Clear();
        FormAddIns.AddRange(addIns.Select(a => new IngredientLineRow(a.Ingredient, a.Grams)));
    }

    /// <summary><paramref name="lines"/> with each ingredient swapped for its refreshed copy when that differs; unchanged lines are kept as they are.</summary>
    private static IReadOnlyList<SubAssemblyLineEdit> Repriced(IReadOnlyList<SubAssemblyLineEdit> lines, IReadOnlyDictionary<int, RawMaterial> ingredientsById) =>
        [.. lines.Select(l => ingredientsById.TryGetValue(l.Ingredient.Id, out var fresh) && !CollectionMerge.HasSameValues(fresh, l.Ingredient)
            ? new SubAssemblyLineEdit(fresh, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)
            : l)];

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
        HasMaterialCost = material > 0;
        ExampleResult = null;
    }

    /// <summary>The cost summary, from the recipe and packaging as they are now and the costing fields.</summary>
    private void RecomputeCosts() =>
        Costs = new ProductCostSummary(
            [.. UsageEdits.Select(u => u.CostPerPiece)],
            [.. LineEdits.Select(l => l.CostPerPiece)],
            EditingLaborPercent,
            EditingOverheadPercent,
            EditingAdministrativePercent,
            EditingMarginPercent,
            EditingVatPercent);
}
