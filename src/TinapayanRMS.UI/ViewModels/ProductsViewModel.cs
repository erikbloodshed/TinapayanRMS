using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
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
/// One sub-assembly in the product's recipe, saved or not: a section of the Recipe Structure, called by its
/// role, and by its name too when the recipe has more than one in that role (<see cref="SectionName"/>), with its
/// ingredient lines, and a box in the Unit Conversion Overview (a batch in pieces). Either new (the form edits its
/// lines, and Save keeps it on the Sub-Assemblies screen), or one the product links (read-only lines, edited on the
/// Sub-Assemblies screen, at the product's own piece size). Its batch totals come from its lines, so they follow its
/// ingredients' costs.
/// </summary>
public sealed class ProductUsageEdit : ReactiveObject
{
    /// <summary>
    /// Sections show in role order (SubAssemblyRole.SortOrder); several in one role, in the order they were saved,
    /// then any not saved yet.
    /// </summary>
    public static IComparer<ProductUsageEdit> RecipeOrder { get; } = Comparer<ProductUsageEdit>.Create((a, b) =>
    {
        var byRole = (a.Role?.SortOrder ?? int.MaxValue).CompareTo(b.Role?.SortOrder ?? int.MaxValue);
        if (byRole == 0)
        {
            byRole = StringComparer.CurrentCultureIgnoreCase.Compare(a.RoleName, b.RoleName);
        }

        return byRole != 0 ? byRole : SavedOrder(a).CompareTo(SavedOrder(b));
    });

    private static int SavedOrder(ProductUsageEdit usage) => usage.SubAssemblyId > 0 ? usage.SubAssemblyId : int.MaxValue;

    /// <param name="subAssemblyId">The sub-assembly's id, or 0 for a new section, not saved yet.</param>
    /// <param name="linkedName">The linked sub-assembly's name, e.g. "Cheese Roll Filling: Ube"; null for the product's own or a new section.</param>
    /// <param name="addIns">This product's own extra ingredients on top of a linked recipe; always empty for its own or a new section.</param>
    /// <param name="name">
    /// What it goes by beside its role: the optional name typed for a new section, or a linked one's name less this
    /// product's own and its role (SubAssemblyRules.SectionLabel), "Ube".
    /// </param>
    /// <param name="shrinkagePercent">What the batch loses being made, as a share of itself; the pieces come out of what's left.</param>
    public ProductUsageEdit(
        int subAssemblyId,
        SubAssemblyRole? role,
        IReadOnlyList<SubAssemblyLineEdit> lines,
        UsageAuthoredField authoredField,
        decimal authoredValue,
        decimal shrinkagePercent,
        string? linkedName,
        IReadOnlyList<SubAssemblyLineEdit>? addIns = null,
        string? name = null)
    {
        Name = SubAssemblyRules.NormalizeName(name);
        SubAssemblyId = subAssemblyId;
        Role = role;
        Lines = lines;
        AuthoredField = authoredField;
        AuthoredValue = authoredValue;
        ShrinkagePercent = shrinkagePercent;
        LinkedName = linkedName;
        AddIns = addIns ?? [];

        // The sub-assembly's own batch, then this product's on top of it: add-ins go into the batch, so a
        // piece takes its weight-per-piece share of the two together.
        (BaseTotalWeight, var baseCost) = SubAssemblyRules.CalculateTotals([.. lines.Select(l => (l.Grams, l.Ingredient.BufferedCostPerBaseUnit))]);
        (TotalWeight, TotalCost) = ProductRules.CalculateEffectiveBatch(
            BaseTotalWeight, baseCost, [.. AddIns.Select(a => (a.Grams, a.Ingredient.BufferedCostPerBaseUnit))]);

        // The pieces come out of what the batch makes, not out of what went into it: the batch keeps its whole
        // cost, so shrinking it leaves fewer, dearer pieces.
        FinalWeight = ProductRules.CalculateFinalWeight(TotalWeight, shrinkagePercent);
        (WeightPerPiece, Yield) = ProductRules.ResolvePieceSize(authoredField, authoredValue, FinalWeight);

        // A batch that weighs nothing has nothing to share out per piece.
        if (FinalWeight > 0 && WeightPerPiece > 0)
        {
            CostPerPiece = ProductRules.CalculateUsageCostPerPiece(TotalCost, WeightPerPiece, FinalWeight);
        }
    }

    public int SubAssemblyId { get; }

    public SubAssemblyRole? Role { get; }

    public int RoleId => Role?.Id ?? 0;

    public string RoleName => Role?.Name ?? string.Empty;

    /// <summary>
    /// What it goes by beside its role, e.g. "Ube": the name typed for a new section, which it needn't have, or a linked
    /// one's name less this product's own and its role, and nothing when that's all its name is (SubAssemblyRules.SectionLabel).
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// What it goes by beside its role when the recipe has more than one in the role: <see cref="Name"/>, which one in a
    /// role can go without.
    /// </summary>
    public string? Label => Name;

    private bool _isRoleRepeated;
    /// <summary>
    /// Whether the recipe has other sections in its role, so this one is called by its name as well. The screen sets it
    /// as the recipe changes (ProductsViewModel.MarkRepeatedRoles).
    /// </summary>
    public bool IsRoleRepeated
    {
        get => _isRoleRepeated;
        set
        {
            this.RaiseAndSetIfChanged(ref _isRoleRepeated, value);
            this.RaisePropertyChanged(nameof(SectionName));
            this.RaisePropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>
    /// What the section is called: its tab and its heading. Its role, e.g. "Filling",
    /// and with its name when the recipe has more than one in the role: "Filling: Ube" beside "Filling: Cheese".
    /// </summary>
    public string SectionName => IsRoleRepeated && Label is { } label ? $"{RoleName}: {label}" : RoleName;

    public IReadOnlyList<SubAssemblyLineEdit> Lines { get; }

    /// <summary>
    /// This product's own extra ingredients on top of a recipe it links — a colouring, a flavouring, an
    /// inclusion. Empty for a new section, whose lines the form edits directly.
    /// </summary>
    public IReadOnlyList<SubAssemblyLineEdit> AddIns { get; }

    /// <summary>Which figure the user typed; it stays put when the batch changes, and the other follows.</summary>
    public UsageAuthoredField AuthoredField { get; }

    public decimal AuthoredValue { get; }

    /// <summary>What the batch loses being made, as a percentage of itself; 0 when nothing is lost.</summary>
    public decimal ShrinkagePercent { get; }

    /// <summary>Where every sub-assembly is kept, and edited: the Sub-Assemblies screen.</summary>
    public const string StandaloneSourceName = "Sub-Assemblies";

    /// <summary>The linked sub-assembly's name, e.g. "Cheese Roll Filling: Ube"; null for the product's own or a new section.</summary>
    public string? LinkedName { get; }

    /// <summary>
    /// The form edits its lines: the product's own (<see cref="IsOwned"/>), or a new section, which Save makes the
    /// product's own in a product-owned role, and otherwise keeps on the Sub-Assemblies screen and links.
    /// </summary>
    public bool IsEditable => LinkedName is null;

    /// <summary>Not saved yet.</summary>
    public bool IsNew => SubAssemblyId == 0;

    /// <summary>
    /// The product's own saved sub-assembly, in a product-owned role (SubAssemblyRole.IsProductOwned): edited here, not on
    /// the Sub-Assemblies screen, and deleted when it's taken out of the recipe.
    /// </summary>
    public bool IsOwned => SubAssemblyId > 0 && LinkedName is null;

    public bool IsLinked => LinkedName is not null;



    /// <summary>The badge beside a linked section's heading, which opens it on the Sub-Assemblies screen: "Cheese Roll Dough, from Sub-Assemblies".</summary>
    public string LinkedFromText => $"{LinkedName}, from {StandaloneSourceName}";

    /// <summary>What it's called where the recipe it's in isn't plain: a linked one by its name, a new one as its section is.</summary>
    public string DisplayName => LinkedName ?? SectionName;

    /// <summary>The sub-assembly's own batch, before this product's add-ins: what the form's caption compares against.</summary>
    public decimal BaseTotalWeight { get; }

    /// <summary>The batch a piece is cut from: the sub-assembly's own plus this product's add-ins.</summary>
    public decimal TotalWeight { get; }

    public decimal TotalCost { get; }

    /// <summary>What the batch weighs once it's made: TotalWeight less its shrinkage. The pieces are cut from this.</summary>
    public decimal FinalWeight { get; }

    /// <summary>Grams of the finished batch in one piece.</summary>
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

    /// <summary>A new section with its role refreshed (renamed in Settings) and its lines repriced with their ingredients.</summary>
    public ProductUsageEdit With(SubAssemblyRole? role, IReadOnlyList<SubAssemblyLineEdit> lines) =>
        new(SubAssemblyId, role, lines, AuthoredField, AuthoredValue, ShrinkagePercent, LinkedName, AddIns, Name);

    /// <summary>
    /// A linked section as the Sub-Assemblies screen now has it — its name, the label it gives (<paramref name="name"/>),
    /// its role and lines — with its add-ins repriced; its piece size stays the product's.
    /// </summary>
    public ProductUsageEdit WithLinked(
        string linkedName, string? name, SubAssemblyRole? role, IReadOnlyList<SubAssemblyLineEdit> lines, IReadOnlyList<SubAssemblyLineEdit> addIns) =>
        new(SubAssemblyId, role, lines, AuthoredField, AuthoredValue, ShrinkagePercent, linkedName, addIns, name);

    /// <summary>The same linked section at another piece size and shrinkage, with the add-ins the form now holds.</summary>
    public ProductUsageEdit WithPieceSizeAndAddIns(
        UsageAuthoredField authoredField, decimal authoredValue, decimal shrinkagePercent, IReadOnlyList<SubAssemblyLineEdit> addIns) =>
        new(SubAssemblyId, Role, Lines, authoredField, authoredValue, shrinkagePercent, LinkedName, addIns, Name);

    /// <summary>Whether <paramref name="other"/> shows the same thing: the same role and names, piece size, and lines and add-ins at the same costs.</summary>
    public bool ShowsSameAs(ProductUsageEdit other) =>
        SubAssemblyId == other.SubAssemblyId
        && CollectionMerge.HasSameValues(Role, other.Role)
        && LinkedName == other.LinkedName
        && Name == other.Name
        && AuthoredField == other.AuthoredField
        && AuthoredValue == other.AuthoredValue
        && ShrinkagePercent == other.ShrinkagePercent
        && Lines.Count == other.Lines.Count
        && Lines.Zip(other.Lines).All(pair =>
            pair.First.Grams == pair.Second.Grams
            && pair.First.IsFormulaBase == pair.Second.IsFormulaBase
            && pair.First.AuthoredBakersPercent == pair.Second.AuthoredBakersPercent
            && CollectionMerge.HasSameValues(pair.First.Ingredient, pair.Second.Ingredient))
        && AddIns.Count == other.AddIns.Count
        && AddIns.Zip(other.AddIns).All(pair => pair.First.Grams == pair.Second.Grams && CollectionMerge.HasSameValues(pair.First.Ingredient, pair.Second.Ingredient));
}

/// <summary>
/// Where the sub-assembly being added comes from, picked on Add Sub-Assembly's drop-down
/// (RecipeStructureCard) and carried by AddUsageCommand. New is the default, which is what a
/// parameterless Execute() passes.
/// </summary>
public enum UsageFormMode
{
    /// <summary>A new sub-assembly, kept on the Sub-Assemblies screen once saved, and linked.</summary>
    New,

    /// <summary>An existing sub-assembly, used as it is and following its changes.</summary>
    Link,

    /// <summary>A new sub-assembly starting from an existing one's role and lines; nothing connects the two afterward.</summary>
    Copy,
}

/// <summary>
/// A choice in the Link and Copy picker: a recipe sub-assembly, "Chocolate Glaze — Sub-Assemblies (Glaze)", or
/// packaging: a packaging set, "Gift Box Set — Sub-Assemblies (Packaging)", or another product's, "Packaging — Cream Loaf".
/// </summary>
public sealed class LinkableSubAssemblyOption
{
    /// <summary>A sub-assembly: a recipe, or a packaging set (one in the Packaging role).</summary>
    public LinkableSubAssemblyOption(SubAssembly subAssembly)
    {
        SubAssembly = subAssembly;
        if (subAssembly.Role?.IsPackaging == true)
        {
            PackagingTarget = PackagingLinkTarget.ForSet(subAssembly.Id, subAssembly.Name);
        }
    }

    /// <summary>Another product's packaging, whatever it is: its own lines, or what it links in turn.</summary>
    public LinkableSubAssemblyOption(Product product)
    {
        PackagingTarget = PackagingLinkTarget.ForProduct(product.Id, product.Name);
    }

    /// <summary>The sub-assembly, or null for another product's packaging.</summary>
    public SubAssembly? SubAssembly { get; }

    /// <summary>What the product's packaging would link or copy, or null for a recipe.</summary>
    public PackagingLinkTarget? PackagingTarget { get; }

    public bool IsPackaging => PackagingTarget is not null;

    /// <summary>A sub-assembly's id, or another product's id negated: one key for the picker's merge.</summary>
    public int Key => SubAssembly?.Id ?? PackagingTarget!.Key;

    public string DisplayName => SubAssembly is null
        ? $"Packaging — {PackagingTarget!.Name}"
        : $"{SubAssembly.Name} — {SubAssembly.OwnerProduct?.Name ?? ProductUsageEdit.StandaloneSourceName} ({SubAssembly.Role?.Name})";
}

/// <summary>
/// A product's cost for one sellable unit, from its materials to its selling price (cost-plus,
/// CostingRules), with each part's share of the selling price. The recipe is authored per piece, so the
/// usages are multiplied by <c>quantityRequired</c>; the packaging lines already wrap one whole unit.
/// Worked out from the form, so unsaved edits show at once.
/// </summary>
public sealed class ProductCostSummary
{
    public static ProductCostSummary Empty { get; } = new([], 1, [], 0m, 0m, 0m, 0m, 0m, null);

    public ProductCostSummary(
        IReadOnlyList<decimal> usageCostsPerPiece,
        int quantityRequired,
        IReadOnlyList<decimal> packagingCosts,
        decimal laborPercent,
        decimal overheadPercent,
        decimal administrativePercent,
        decimal marginPercent,
        decimal vatPercent,
        decimal? currentPrice)
    {
        QuantityRequired = quantityRequired;
        SubAssemblies = usageCostsPerPiece.Sum() * quantityRequired;
        Packaging = packagingCosts.Sum();
        Material = ProductRules.CalculateMaterialCost(usageCostsPerPiece, quantityRequired, packagingCosts);
        LaborPercent = laborPercent;
        OverheadPercent = overheadPercent;
        AdministrativePercent = administrativePercent;
        Labor = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, laborPercent);
        Overhead = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, overheadPercent);
        Administrative = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, administrativePercent);
        MarginPercent = marginPercent;
        VatPercent = vatPercent;
        (BaseCost, PreVatPrice, SellingPrice) = CostingRules.CalculateSellingPrice(
            SubAssemblies, Packaging, laborPercent, overheadPercent, administrativePercent, marginPercent, vatPercent);
        CurrentPrice = currentPrice;
        CurrentMarkupPercent = currentPrice is { } price
            ? CostingRules.CalculateMarkupPercentFromPrice(BaseCost, price, vatPercent)
            : null;
    }

    /// <summary>How many finished pieces this unit holds: 1 for a product sold by the piece.</summary>
    public int QuantityRequired { get; }

    public decimal SubAssemblies { get; }

    public decimal Packaging { get; }

    public decimal Material { get; }

    public decimal LaborPercent { get; }

    public decimal OverheadPercent { get; }

    public decimal AdministrativePercent { get; }

    /// <summary>The labor cost per sellable unit, in pesos: LaborPercent of the sub-assembly cost.</summary>
    public decimal Labor { get; }

    /// <summary>The overhead cost per sellable unit, in pesos: OverheadPercent of the sub-assembly cost.</summary>
    public decimal Overhead { get; }

    /// <summary>The administrative cost per sellable unit, in pesos: AdministrativePercent of the sub-assembly cost.</summary>
    public decimal Administrative { get; }

    /// <summary>The subtotal the markup goes on: materials, labor, overhead and administrative.</summary>
    public decimal BaseCost { get; }

    public decimal MarginPercent { get; }

    public decimal PreVatPrice { get; }

    public decimal VatPercent { get; }

    public decimal SellingPrice { get; }

    /// <summary>The markup: the profit on one sellable unit, since VAT is collected for the government.</summary>
    public decimal Markup => PreVatPrice - BaseCost;

    public decimal Vat => SellingPrice - PreVatPrice;

    /// <summary>What the shop charges for one sellable unit now, with VAT, as entered on the form; null when not entered.</summary>
    public decimal? CurrentPrice { get; }

    /// <summary>
    /// The markup CurrentPrice actually earns on the base cost, VAT taken out (as MarginPercent is on
    /// SellingPrice); null when no current price is entered.
    /// </summary>
    public decimal? CurrentMarkupPercent { get; }

    /// <summary>The current price doesn't cover the base cost: a negative markup, which the header shows in red.</summary>
    public bool IsCurrentPriceBelowCost => CurrentMarkupPercent < 0;

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
/// The Products screen: a product's recipe (the sub-assemblies it links and new ones not saved yet, each at its own
/// piece size, and its packaging), its costing, its packaging link, and cloning it or making a variant of it. The name, description,
/// recipe, packaging and costing are edited in memory, and Save writes them together
/// (IProductRepository.SaveAsync).
/// </summary>
public sealed class ProductsViewModel : ViewModelBase, IUndoScreen, IDisposable
{
    /// <summary>The details tabs along the bottom: Recipe, Costing, then Clone.</summary>
    public const int RecipeTab = 0;

    /// <summary>Recipe 0, Costing 1, then Clone.</summary>
    public const int LinksTab = 2;


    private readonly IProductRepository _productRepository;
    private readonly IProductSubAssemblyUsageRepository _usageRepository;
    private readonly IPackagingLineRepository _packagingLineRepository;
    private readonly ICostingParametersRepository _costingParametersRepository;
    private readonly ICostingDefaultsRepository _costingDefaultsRepository;
    private readonly IPackagingLinkRepository _packagingLinkRepository;
    private readonly IProductCloner _productCloner;
    private readonly ISubAssemblyRepository _subAssemblyRepository;
    private readonly ISubAssemblyLineRepository _subAssemblyLineRepository;
    private readonly IValidator<Product> _productValidator;
    private readonly IValidator<CostingParameters> _costingParametersValidator;
    private readonly IProductCategoryRepository _categoryRepository;
    private readonly IValidator<ProductCategory> _categoryValidator;
    private readonly SourceCache<Product, int> _productCache = new(p => p.Id);
    private readonly ReadOnlyObservableCollection<Product> _products;
    private readonly ReadOnlyObservableCollection<Product> _visibleProducts;
    private readonly SourceCache<ProductCategory, int> _categoryCache = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<ProductCategory> _categories;
    private readonly SourceCache<ProductCategory, int> _addNewCategoryOption = new(c => c.Id);
    private readonly ReadOnlyObservableCollection<ProductCategory> _categoryOptions;

    /// <summary>Every product, for the packaging link and clone pickers.</summary>
    public ReadOnlyObservableCollection<Product> Products => _products;

    /// <summary>The products the list shows: those whose name matches SearchText.</summary>
    public ReadOnlyObservableCollection<Product> VisibleProducts => _visibleProducts;

    public ReadOnlyObservableCollection<ProductCategory> Categories => _categories;

    /// <summary>
    /// The Category picker's "&lt; Add New .. &gt;" row, as on Raw Materials (RawMaterialsViewModel.AddNewCategoryId):
    /// a category-shaped stand-in for adding one, told apart by its Id and kept out of Categories.
    /// </summary>
    public const int AddNewCategoryId = -1;

    public static readonly ProductCategory AddNewCategory = new() { Id = AddNewCategoryId, Name = "< Add New .. >" };

    /// <summary>What the Category picker lists: the categories, with the "&lt; Add New .. &gt;" row above them.</summary>
    public ReadOnlyObservableCollection<ProductCategory> CategoryOptions => _categoryOptions;

    public ObservableCollection<SubAssemblyRole> Roles { get; } = [];

    /// <summary>
    /// The roles the sub-assembly form offers: every recipe role, as a recipe can have several sub-assemblies in one,
    /// told apart by their names; and the Packaging role while there's no packaging.
    /// </summary>
    public ObservableCollection<SubAssemblyRole> FormRoleOptions { get; } = [];

    /// <summary>The ingredients only, for a sub-assembly's lines: packaging never goes in a recipe.</summary>
    public ObservableCollection<RawMaterial> Ingredients { get; } = [];

    /// <summary>The packaging materials only: ingredients never go on a packaging line.</summary>
    public ObservableCollection<RawMaterial> PackagingMaterials { get; } = [];

    /// <summary>Every sub-assembly, saved or not, its own and linked, in recipe order (ProductUsageEdit.RecipeOrder).</summary>
    public ObservableCollection<ProductUsageEdit> UsageEdits { get; } = [];

    /// <summary>
    /// The Recipe Structure's tabs: a sub-assembly's for each of UsageEdits, at the same index (MirrorUsageTabs),
    /// then AddSubAssemblyTab while a sub-assembly is being added, then PackagingTab while the product has packaging
    /// (HasPackaging). A new product has none. Only this screen changes them: the tab strip can't drag them into
    /// another order (RecipeStructureCard).
    /// </summary>
    public ObservableCollection<RecipeTab> RecipeTabs { get; } = [];

    /// <summary>The Recipe Structure has a tab to show; with none, the card says how to start.</summary>
    public bool HasRecipeTabs => RecipeTabs.Count > 0;

    /// <summary>The New Sub-Assembly tab, in RecipeTabs only while Add Sub-Assembly's form is open in it.</summary>
    public AddSubAssemblyRecipeTab AddSubAssemblyTab { get; } = new();

    public PackagingRecipeTab PackagingTab { get; } = new();

    private RecipeTab? _selectedRecipeTab;
    /// <summary>The Recipe Structure tab showing. Showing New Sub-Assembly opens the form that adds one.</summary>
    public RecipeTab? SelectedRecipeTab
    {
        get => _selectedRecipeTab;
        set => this.RaiseAndSetIfChanged(ref _selectedRecipeTab, value);
    }

    private readonly ObservableAsPropertyHelper<bool> _isUsageFormShowing;
    /// <summary>
    /// The sub-assembly form shows under the Recipe Structure's tabs: on a sub-assembly's tab, and on New
    /// Sub-Assembly's while its form is open. There's one form for them all (RecipeStructureCard).
    /// </summary>
    public bool IsUsageFormShowing => _isUsageFormShowing.Value;

    private readonly ObservableAsPropertyHelper<bool> _isPackagingShowing;
    /// <summary>The packaging shows under the Recipe Structure's tabs: on the Packaging tab.</summary>
    public bool IsPackagingShowing => _isPackagingShowing.Value;

    /// <summary>The last tab other than New Sub-Assembly that showed, for cancelling an add.</summary>
    private RecipeTab? _lastRecipeContentTab;

    /// <summary>The tab whose form is open: the one a change of tab is leaving, whose form is applied on the way out.</summary>
    private RecipeTab? _shownRecipeTab;

    /// <summary>Set while a refused form puts the tab showing back, so the tab it puts back doesn't apply its own form again.</summary>
    private bool _revertingRecipeTab;

    /// <summary>
    /// Set while the sections are rebuilt, or New Sub-Assembly's tab taken away. The tab strip picks a neighbour
    /// when the tab showing is removed; that pick is ignored, and ReselectRecipeTab picks one after.
    /// </summary>
    private bool _arrangingRecipeTabs;

    /// <summary>
    /// Set while SelectRecipeTab's pick is on its way to the tab strip, which hears of it after this screen does:
    /// New Sub-Assembly's tab can't be taken away until the strip has moved off it, or the strip would pick a
    /// neighbour of its own in place of this one.
    /// </summary>
    private bool _selectingRecipeTab;

    /// <summary>
    /// The recipe showing was cleared for another product's, which opens on its first tab once it arrives
    /// (ReplaceUsages). What the tab strip was left on as the tabs went says nothing: that's always Packaging.
    /// </summary>
    private bool _isRecipeClearedForAnother;

    /// <summary>The packaging lines, typed straight into the Packaging tab's table.</summary>
    public ObservableCollection<PackagingLineRow> LineEdits { get; } = [];

    /// <summary>The line the packaging table's add row edits: it offers its material's units and prices itself before it's committed.</summary>
    public Func<PackagingLineRow> NewLineEdit => () => new PackagingLineRow(FindPackagingMaterial);

    /// <summary>A packaging material as the screen last loaded it, for a row that prices itself.</summary>
    private RawMaterial? FindPackagingMaterial(int id) => PackagingMaterials.FirstOrDefault(m => m.Id == id);

    /// <summary>The standalone packaging sets (sub-assemblies in the Packaging role), by name, as last loaded, for the form's picker.</summary>
    private IReadOnlyList<SubAssembly> _packagingSets = [];

    /// <summary>
    /// What the per-piece packaging links as the form stands, saved or not (the Add Sub-Assembly form's Link sets it,
    /// the Packaging tab's Make a Copy and Delete Sub-Assembly clear it), or null while the product owns its lines.
    /// </summary>
    private PackagingLinkTarget? _packagingLinkTarget;

    /// <summary>The key of the link last loaded or saved, or null for none: Save writes the link only when it differs.</summary>
    private int? _savedPackagingLinkKey;

    /// <summary>
    /// The add form's packaging lines, when it adds packaging (IsFormPackaging): how much of each material one piece
    /// takes. Typed into for a new or copied packaging; read-only for a link, whose lines are kept where it's from.
    /// </summary>
    public ObservableCollection<PackagingLineRow> FormPackagingLines { get; } = [];

    /// <summary>The line the form's packaging table's add row edits, priced as its material is picked.</summary>
    public Func<PackagingLineRow> NewFormPackagingLine => () => new PackagingLineRow(FindPackagingMaterial);

    private PackagingLineRow? _selectedFormPackagingLine;
    public PackagingLineRow? SelectedFormPackagingLine
    {
        get => _selectedFormPackagingLine;
        set => this.RaiseAndSetIfChanged(ref _selectedFormPackagingLine, value);
    }

    /// <summary>
    /// The add form adds the product's packaging rather than a recipe section: the Packaging role is picked for a new one,
    /// or a packaging source for a link or a copy. It then shows the packaging table in place of the ingredients, add-ins
    /// and piece size, and Apply fills the Packaging tab.
    /// </summary>
    public bool IsFormPackaging => IsAddingUsage && EditingUsageMode switch
    {
        UsageFormMode.Link => EditingUsageSource?.IsPackaging == true,
        UsageFormMode.Copy => EditingUsageSource is { } source ? source.IsPackaging : EditingUsageRole?.IsPackaging == true,
        _ => EditingUsageRole?.IsPackaging == true,
    };

    /// <summary>The form's packaging table can be typed into: a new or copied packaging, not a link.</summary>
    public bool IsFormEditingPackaging => IsFormPackaging && EditingUsageMode != UsageFormMode.Link;

    /// <summary>
    /// Other products' sub-assemblies the form can link or copy. Linking leaves out the ones the recipe
    /// already uses; a product never links or copies its own.
    /// </summary>
    public ObservableCollection<LinkableSubAssemblyOption> LinkableSubAssemblies { get; } = [];

    /// <summary>The sub-assembly form's lines, for a new section: typed straight into its table.</summary>
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

    /// <summary>A product <see cref="OpenProduct"/> was asked for before the list held it, for LoadCommand to open once it does.</summary>
    private int? _productToOpen;

    private int _editingId;

    /// <summary>The product whose recipe, packaging, costing and links the form holds, or null for a new product.</summary>
    private int? _detailProductId;

    private bool _areDetailsReady = true;
    /// <summary>
    /// The details showing are the picked product's. False from picking another product until its details
    /// arrive (ResetDetails to LoadProductDetailAsync): the previous product's stay showing meanwhile, so no
    /// empty product is drawn between the two, but they're no longer the ones the form is about, so nothing
    /// may act on them (the details tabs ignore the mouse, and Save does nothing).
    /// </summary>
    public bool AreDetailsReady
    {
        get => _areDetailsReady;
        private set => this.RaiseAndSetIfChanged(ref _areDetailsReady, value);
    }

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

    private int _editingQuantityRequired = 1;
    /// <summary>
    /// How many finished pieces make one sellable unit (Product.QuantityRequired): 1 for a product sold by the
    /// piece, 8 for a "Box of 8". The recipe stays per piece; every cost and price on this screen is for one
    /// whole unit, so this multiplies the sub-assemblies' share of it.
    /// </summary>
    public int EditingQuantityRequired
    {
        get => _editingQuantityRequired;
        set => this.RaiseAndSetIfChanged(ref _editingQuantityRequired, value);
    }

    /// <summary>What the figures on this screen are for: "Per piece", or "Per unit of 8 pieces".</summary>
    public string CostUnitCaption => EditingQuantityRequired <= 1 ? "Per piece" : $"Per unit of {EditingQuantityRequired} pieces";

    private decimal? _editingCurrentPrice;
    /// <summary>What the shop charges for one sellable unit now, with VAT (Product.CurrentPrice); null when not entered.</summary>
    public decimal? EditingCurrentPrice
    {
        get => _editingCurrentPrice;
        set => this.RaiseAndSetIfChanged(ref _editingCurrentPrice, value);
    }

    private string _categorySearchText = string.Empty;
    /// <summary>
    /// What's typed into the Category picker (bound to its Text). Renames the "&lt; Add New .. &gt;" row to include
    /// it, so the row survives the picker's own AutoComplete filtering, as on Raw Materials.
    /// </summary>
    public string CategorySearchText
    {
        get => _categorySearchText;
        set => this.RaiseAndSetIfChanged(ref _categorySearchText, value);
    }

    private ProductCategory? _editingCategory;
    /// <summary>The product's category, or null for none: a product doesn't need one.</summary>
    public ProductCategory? EditingCategory
    {
        get => _editingCategory;
        set
        {
            // The "< Add New .. >" row isn't a category to put the product in: AddCategoryCommand takes over, and puts
            // a category back in the picker once it's done. By id, since the row is replaced as CategorySearchText changes.
            if (value?.Id == AddNewCategoryId)
            {
                AddCategoryCommand.Execute().Subscribe();
                return;
            }

            this.RaiseAndSetIfChanged(ref _editingCategory, value);
        }
    }

    private readonly ObservableAsPropertyHelper<string> _headerTitle;
    /// <summary>The header's title, and the usage example's: the product's name, or "New product".</summary>
    public string HeaderTitle => _headerTitle.Value;

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

    /// <summary>The details tab showing. Moves to Recipe when Clone is about to hide (ShowLinks).</summary>
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
        private set
        {
            this.RaiseAndSetIfChanged(ref _isPackagingLinked, value);
            this.RaisePropertyChanged(nameof(IsPackagingEditable));
        }
    }

    /// <summary>The packaging is the product's own, so its table can be typed into; linked packaging is kept where it's from.</summary>
    public bool IsPackagingEditable => !IsPackagingLinked;

    private string _packagingLinkedToText = string.Empty;
    /// <summary>"Linked to Cream Loaf" or "Linked to Gift Box Set (Sub-Assemblies)", or empty while the product has its own packaging.</summary>
    public string PackagingLinkedToText
    {
        get => _packagingLinkedToText;
        private set => this.RaiseAndSetIfChanged(ref _packagingLinkedToText, value);
    }

    /// <summary>The packaging set linked packaging comes from, whose badge opens it on Sub-Assemblies; null for another product's, or none.</summary>
    public int? PackagingLinkedSetId => _packagingLinkTarget?.SubAssemblyId;

    public bool IsPackagingLinkedToSet => PackagingLinkedSetId is not null;

    public bool IsPackagingLinkedToProduct => IsPackagingLinked && !IsPackagingLinkedToSet;

    private bool _hasPackaging;
    /// <summary>
    /// The product has packaging, its own lines or a link, so the Recipe Structure has its Packaging tab. A new product
    /// has none until Add Sub-Assembly adds it in the Packaging role; Delete Sub-Assembly on the tab takes it out.
    /// </summary>
    public bool HasPackaging
    {
        get => _hasPackaging;
        private set => this.RaiseAndSetIfChanged(ref _hasPackaging, value);
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

    private string _usageFormTitle = "New sub-assembly";
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

    private UsageFormMode _editingUsageMode;
    /// <summary>Which way the add form takes its sub-assembly: what Add Sub-Assembly's drop-down picked.</summary>
    public UsageFormMode EditingUsageMode
    {
        get => _editingUsageMode;
        set => this.RaiseAndSetIfChanged(ref _editingUsageMode, value);
    }

    private bool _isFormEditingLines = true;
    /// <summary>The form edits a new section: its name and role show, and its lines are typed into.</summary>
    public bool IsFormEditingLines
    {
        get => _isFormEditingLines;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isFormEditingLines, value);
            this.RaisePropertyChanged(nameof(IsFormShowingLines));
            this.RaisePropertyChanged(nameof(IsFormOfferingAddIns));
            this.RaisePropertyChanged(nameof(IsFormShowingAddIns));
        }
    }

    /// <summary>
    /// The form's lines table shows: the product's own lines, to type into, or a linked one's, read-only. A link
    /// has none to show until the sub-assembly it comes from has been picked and its lines loaded.
    /// </summary>
    public bool IsFormShowingLines => IsFormEditingLines || FormLines.Count > 0;

    /// <summary>
    /// The form can take add-ins: only for a sub-assembly the product links, and only once the recipe it sits on
    /// top of is there to sit on. A sub-assembly of the product's own has no add-ins — its extras just go in its
    /// lines. Show Add-Ins is offered then.
    /// </summary>
    public bool IsFormOfferingAddIns => !IsFormEditingLines && IsFormShowingLines;

    private bool _isFormAddInsExpanded;
    /// <summary>
    /// Show Add-Ins opened the add-ins table. A linked sub-assembly's form opens with it open only when it has
    /// add-ins: most links have none, and an empty table only took room. A refused add-in opens it, so the
    /// message has its line to point at.
    /// </summary>
    public bool IsFormAddInsExpanded
    {
        get => _isFormAddInsExpanded;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isFormAddInsExpanded, value);
            this.RaisePropertyChanged(nameof(IsFormShowingAddIns));
            this.RaisePropertyChanged(nameof(FormAddInsToggleText));
        }
    }

    /// <summary>The form's add-ins table shows: it can take add-ins, and Show Add-Ins has opened it.</summary>
    public bool IsFormShowingAddIns => IsFormOfferingAddIns && IsFormAddInsExpanded;

    /// <summary>The Show Add-Ins button: "Show Add-Ins", "Show Add-Ins (2)" while two are out of sight, or "Hide Add-Ins".</summary>
    public string FormAddInsToggleText =>
        IsFormAddInsExpanded ? "Hide Add-Ins" : FormAddIns.Count > 0 ? $"Show Add-Ins ({FormAddIns.Count})" : "Show Add-Ins";

    private bool _isFormPickingSource;
    /// <summary>The form links or copies an existing sub-assembly (another product's, or a standalone one): its picker shows.</summary>
    public bool IsFormPickingSource
    {
        get => _isFormPickingSource;
        private set => this.RaiseAndSetIfChanged(ref _isFormPickingSource, value);
    }

    private SubAssemblyRole? _editingUsageRole;
    /// <summary>The role of the new section the form edits, which is what it's called. Picked from FormRoleOptions.</summary>
    public SubAssemblyRole? EditingUsageRole
    {
        get => _editingUsageRole;
        set => this.RaiseAndSetIfChanged(ref _editingUsageRole, value);
    }

    private string? _editingUsageName;
    /// <summary>
    /// The optional name of the new section the form edits: what tells it apart from others in its role,
    /// its tab reading "Filling: Ube" beside "Filling: Cheese".
    /// </summary>
    public string? EditingUsageName
    {
        get => _editingUsageName;
        set => this.RaiseAndSetIfChanged(ref _editingUsageName, value);
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

    private decimal _editingUsageShrinkagePercent;
    /// <summary>
    /// What the batch loses being made, as a percentage of itself. It isn't one of the pair — neither box
    /// is "the one typed" because of it — but the batch the pair divides is the final weight, so a change
    /// here works the other figure out again just as a line does.
    /// </summary>
    public decimal EditingUsageShrinkagePercent
    {
        get => _editingUsageShrinkagePercent;
        set => this.RaiseAndSetIfChanged(ref _editingUsageShrinkagePercent, value);
    }

    private string _usageFormTotalWeightText = "0 g";
    /// <summary>The batch that goes in, e.g. "1,700 g".</summary>
    public string UsageFormTotalWeightText
    {
        get => _usageFormTotalWeightText;
        private set => this.RaiseAndSetIfChanged(ref _usageFormTotalWeightText, value);
    }

    private string _usageFormFinalWeightText = "0 g";
    /// <summary>What the batch makes, which the form's piece size shares out: the total weight less its shrinkage, e.g. "1,530 g".</summary>
    public string UsageFormFinalWeightText
    {
        get => _usageFormFinalWeightText;
        private set => this.RaiseAndSetIfChanged(ref _usageFormFinalWeightText, value);
    }

    private string _usageFormPieceSizeCaption = string.Empty;
    /// <summary>Which figure stays put when the batch changes, e.g. "Weight per piece stays at 60 g if the batch changes".</summary>
    public string UsageFormPieceSizeCaption
    {
        get => _usageFormPieceSizeCaption;
        private set => this.RaiseAndSetIfChanged(ref _usageFormPieceSizeCaption, value);
    }

    private string _usageFormPreviewText = string.Empty;
    /// <summary>What a piece of the form's sub-assembly costs, e.g. "0.5000 a piece": the add form's footer.</summary>
    public string UsageFormPreviewText
    {
        get => _usageFormPreviewText;
        private set => this.RaiseAndSetIfChanged(ref _usageFormPreviewText, value);
    }

    private string _usageFormBaseCaption = string.Empty;
    /// <summary>
    /// The formula in baker's terms, e.g. "Base 1,000 g · whole formula 167.7%" — the figure a batch is scaled by — or,
    /// with nothing ticked, what the absolute percentages are read against.
    /// </summary>
    public string UsageFormBaseCaption
    {
        get => _usageFormBaseCaption;
        private set => this.RaiseAndSetIfChanged(ref _usageFormBaseCaption, value);
    }

    private string _usageFormPercentHeader = IngredientLineRow.PercentHeader(0m);
    /// <summary>Both form tables' percentage header: "Baker's %" while the recipe has a base, "Absolute %" while it has none.</summary>
    public string UsageFormPercentHeader
    {
        get => _usageFormPercentHeader;
        private set => this.RaiseAndSetIfChanged(ref _usageFormPercentHeader, value);
    }

    private string _usageFormAddInsCaption = string.Empty;
    /// <summary>The batch the two tables come to, e.g. "1,700 g from Sweet Dough + 20 g of add-ins = a 1,720 g batch."</summary>
    public string UsageFormAddInsCaption
    {
        get => _usageFormAddInsCaption;
        private set => this.RaiseAndSetIfChanged(ref _usageFormAddInsCaption, value);
    }

    private string _usageFormCostPerPieceText = "—";
    /// <summary>The same figure on its own, e.g. "0.5000": the form header's, for a section being edited.</summary>
    public string UsageFormCostPerPieceText
    {
        get => _usageFormCostPerPieceText;
        private set => this.RaiseAndSetIfChanged(ref _usageFormCostPerPieceText, value);
    }

    private PackagingLineRow? _selectedLineEdit;
    public PackagingLineRow? SelectedLineEdit
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

    private string _editingCloneName = string.Empty;
    public string EditingCloneName
    {
        get => _editingCloneName;
        set => this.RaiseAndSetIfChanged(ref _editingCloneName, value);
    }

    /// <summary>Every sub-assembly that could be linked or copied, as last loaded; LinkableSubAssemblies filters it.</summary>
    private IReadOnlyList<SubAssembly> _linkable = [];

    // What the product had when it was loaded, so Save only sends the parts that changed.
    private RecipeDraft _savedRecipe = new([], []);
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> _savedLines = [];
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) _savedCosting;

    // What a new product's costing starts with (Settings → Costing Defaults), as of the last LoadCommand.
    private (decimal Labor, decimal Overhead, decimal Administrative, decimal Margin, decimal Vat) _costingDefaults;

    public ReactiveCommand<RxVoid, RxVoid> LoadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    /// <summary>
    /// Revert: reads the product back as it was last saved, throwing away everything staged since — the details, the
    /// recipe's sections and their lines, the packaging, the packs and the costing. The sub-assembly form has no
    /// discard of its own, so this is what abandons a section being added or edited, along with the rest.
    /// </summary>
    public ReactiveCommand<RxVoid, RxVoid> RevertCommand { get; }

    /// <summary>
    /// A linked standalone sub-assembly's badge clicked (a recipe section's, or linked packaging's for a packaging set):
    /// asks for the sub-assembly with this id to be opened, through <see cref="SubAssemblyOpenRequests"/>.
    /// </summary>
    public ReactiveCommand<int, RxVoid> OpenSubAssemblyCommand { get; }

    private readonly Subject<int> _subAssemblyOpenRequests = new();

    /// <summary>
    /// The sub-assemblies linked badges ask to open, by id. The main window, which owns the tabs, subscribes and opens
    /// each on the Sub-Assemblies screen, as Sub-Assemblies' Used By tiles open their products here.
    /// </summary>
    public IObservable<int> SubAssemblyOpenRequests => _subAssemblyOpenRequests;

    /// <summary>
    /// The form's undo and redo stacks. One step is one finished change — a cell edit committed, a row added or
    /// taken out, a sub-assembly applied, a field typed and left — never a keystroke. It holds what the screen
    /// shows, not what Save writes: undoing never touches the baselines Save compares against, so a change undone
    /// and one never made come to the same thing. Cleared whenever the form stops being the one it recorded:
    /// another product picked, a new one started, a Save, or a Revert.
    /// </summary>
    public UndoHistory<ProductFormSnapshot> History { get; }

    public ReactiveCommand<RxVoid, RxVoid> UndoCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RedoCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> EditDescriptionCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloseDescriptionEditorCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CancelDescriptionEditCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> AddCategoryCommand { get; }

    /// <summary>
    /// Input: the label over the box, and what was already typed into the Category picker, which the box starts with.
    /// Output: the name typed for the new category, or null if it was cancelled. The View shows a TextPromptWindow.
    /// </summary>
    public Interaction<(string Label, string InitialValue), string?> PromptForNewCategory { get; } = new();

    /// <summary>Adds a sub-assembly the way the parameter says: Add Sub-Assembly's drop-down carries one item per way.</summary>
    public ReactiveCommand<UsageFormMode, RxVoid> AddUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> EditUsageCommand { get; }
    public ReactiveCommand<ProductUsageEdit, RxVoid> MakeCopyCommand { get; }

    /// <summary>Delete Sub-Assembly: takes the sub-assembly whose tab is showing out of the recipe.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveUsageCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveFormLineCommand { get; }

    /// <summary>Show Add-Ins / Hide Add-Ins, under a linked recipe.</summary>
    public ReactiveCommand<RxVoid, RxVoid> ToggleFormAddInsCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RemoveFormAddInCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RemoveLineCommand { get; }

    /// <summary>The add form's Remove Line under its packaging table.</summary>
    public ReactiveCommand<RxVoid, RxVoid> RemoveFormPackagingLineCommand { get; }

    /// <summary>Make a Copy on a linked Packaging tab: the lines it shows become the product's own, and the link goes with Save.</summary>
    public ReactiveCommand<RxVoid, RxVoid> CopyPackagingCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CloneCommand { get; }

    /// <summary>Create Variant on the Clone tab: a new product that links this one's recipe and packaging (IProductCloner.CreateVariantAsync).</summary>
    public ReactiveCommand<RxVoid, RxVoid> CreateVariantCommand { get; }

    public ProductsViewModel(
        IProductRepository productRepository,
        IProductSubAssemblyUsageRepository usageRepository,
        IPackagingLineRepository packagingLineRepository,
        ICostingParametersRepository costingParametersRepository,
        ICostingDefaultsRepository costingDefaultsRepository,
        IPackagingLinkRepository packagingLinkRepository,
        IProductCloner productCloner,
        ISubAssemblyRepository subAssemblyRepository,
        ISubAssemblyLineRepository subAssemblyLineRepository,
        ISubAssemblyRoleRepository subAssemblyRoleRepository,
        IRawMaterialRepository rawMaterialRepository,
        IProductCategoryRepository categoryRepository,
        IValidator<Product> productValidator,
        IValidator<CostingParameters> costingParametersValidator,
        IValidator<ProductCategory> categoryValidator,
        ILogger logger)
        : base(logger)
    {
        _productRepository = productRepository;
        _usageRepository = usageRepository;
        _packagingLineRepository = packagingLineRepository;
        _costingParametersRepository = costingParametersRepository;
        _costingDefaultsRepository = costingDefaultsRepository;
        _packagingLinkRepository = packagingLinkRepository;
        _productCloner = productCloner;
        _subAssemblyRepository = subAssemblyRepository;
        _subAssemblyLineRepository = subAssemblyLineRepository;
        _productValidator = productValidator;
        _costingParametersValidator = costingParametersValidator;
        _categoryRepository = categoryRepository;
        _categoryValidator = categoryValidator;

        // A new product starts with no tabs at all: packaging, like a recipe section, comes in through Add Sub-Assembly.
        RecipeTabs.CollectionChanged += (_, _) => this.RaisePropertyChanged(nameof(HasRecipeTabs));
        UsageEdits.CollectionChanged += (_, e) =>
        {
            MirrorUsageTabs(e);
            MarkRepeatedRoles();
        };

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

        _categoryCache.Connect()
            .SortAndBind(out _categories, SortExpressionComparer<ProductCategory>.Ascending(c => c.Name))
            .Subscribe();

        // The "< Add New .. >" row has a cache of its own, which LoadCommand's merge of the categories never touches.
        _addNewCategoryOption.AddOrUpdate(AddNewCategory);

        _categoryCache.Connect()
            .Or(_addNewCategoryOption.Connect())
            .SortAndBind(out _categoryOptions, SortExpressionComparer<ProductCategory>
                .Ascending(c => c.Id == AddNewCategoryId ? 0 : 1)
                .ThenByAscending(c => c.Name))
            .Subscribe();

        // The picker's AutoComplete filters its rows by what's typed, Add New's too, so the row takes the typed text into
        // its name and always matches (RawMaterialsViewModel explains it in full).
        this.WhenAnyValue(x => x.CategorySearchText)
            .Select(text => text.Trim())
            .DistinctUntilChanged()
            .Subscribe(text => _addNewCategoryOption.AddOrUpdate(new ProductCategory
            {
                Id = AddNewCategoryId,
                Name = text.Length == 0 ? "< Add New .. >" : $"< Add New \"{text}\" .. >",
            }));

        _headerTitle = this.WhenAnyValue(x => x.SelectedProduct, x => x.EditingName, (product, name) =>
                product is null && string.IsNullOrWhiteSpace(name) ? "New product" : name)
            .ToProperty(this, x => x.HeaderTitle, "New product");

        _descriptionLinkText = this.WhenAnyValue(x => x.EditingDescription)
            .Select(description => string.IsNullOrWhiteSpace(description) ? "Add description" : description.Trim())
            .ToProperty(this, x => x.DescriptionLinkText, "Add description");

        // A hidden tab can't stay the one showing: leaving a saved product while on Clone goes
        // back to Recipe. Subscribed before ShowLinks, so the tab strip moves before the tab hides; a
        // TabControlExt whose selected tab hides picks a neighbour itself.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is null && SelectedDetailsTab == LinksTab)
            .Subscribe(_ => SelectedDetailsTab = RecipeTab);

        _showLinks = this.WhenAnyValue(x => x.SelectedProduct)
            .Select(product => product is not null)
            .ToProperty(this, x => x.ShowLinks);

        _isUsageFormShowing = this.WhenAnyValue(
                x => x.SelectedRecipeTab,
                x => x.IsUsageFormOpen,
                (tab, isFormOpen) => tab is SubAssemblyRecipeTab || (tab is AddSubAssemblyRecipeTab && isFormOpen))
            .ToProperty(this, x => x.IsUsageFormShowing);

        _isPackagingShowing = this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Select(tab => tab is PackagingRecipeTab)
            .ToProperty(this, x => x.IsPackagingShowing);

        // Nothing is recorded until a product's details have arrived, or while the screen is being written to
        // wholesale: a load merging in fresh data and a restore putting a form back are neither of them edits.
        // Set up before the subscriptions below rather than beside the Undo and Redo commands: the selection ones
        // run as they're wired, with nothing selected, and the load that fires takes the blank form as the
        // settled one - a history that didn't exist yet would take the whole subscription down with it.
        History = new UndoHistory<ProductFormSnapshot>(
            CaptureSnapshot,
            (before, after) => before.ShowsSameAs(after),
            () => AreDetailsReady && !IsBusyApplying,
            (settled, now) => settled.SavesSameAs(now));

        // Every selection-driven subscription skips while LoadCommand merges in fresh data, which only
        // re-points a selection at its refreshed copy; the form, recipe and unsaved edits stay as-is.
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(product => product is not null && !IsBusyApplying)
            .Subscribe(product => LoadIntoForm(product!));

        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(_ => !IsBusyApplying)
            .Subscribe(ResetDetails);

        // A newer selection cancels an older one still loading (Switch disposes it, which cancels its token).
        this.WhenAnyValue(x => x.SelectedProduct)
            .Where(_ => !IsBusyApplying)
            .Select(product => Observable.FromAsync(cancellationToken => LoadProductDetailAsync(product, cancellationToken)))
            .Switch()
            .Subscribe();

        this.WhenAnyValue(x => x.SelectedRecipeTab)
            .Subscribe(RecipeTabPicked);

        // The sub-assembly form: where its sub-assembly comes from, then the one it links or copies.
        this.WhenAnyValue(x => x.EditingUsageMode)
            .Skip(1)
            .Where(_ => !IsBusyApplying && IsUsageFormOpen && IsAddingUsage)
            .Subscribe(_ => StartUsageFormMode());

        // A line picked, typed or deleted in the form's table: it's priced, and the batch and preview follow.
        FormLines.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .AutoRefresh(l => l.IsFormulaBase)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                IngredientLineRow.Resolve(FormLines, Ingredients.ToDictionary(m => m.Id));
                FormLinesChanged();
            });

        // An add-in picked, typed or deleted: it's priced, and the batch it joins follows.
        FormAddIns.ToObservableChangeSet()
            .AutoRefresh(l => l.IngredientId)
            .AutoRefresh(l => l.Grams)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                IngredientLineRow.Resolve(FormAddIns, Ingredients.ToDictionary(m => m.Id));
                FormLinesChanged();
            });

        // A packaging line picked, typed or deleted in the add form: it's priced, and the preview follows.
        FormPackagingLines.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                PackagingLineRow.Resolve(FormPackagingLines, PackagingMaterials.ToDictionary(m => m.Id));
                RecomputeUsageForm();
            });

        // Whether the form adds packaging follows its mode, role and source.
        this.WhenAnyValue(x => x.IsAddingUsage, x => x.EditingUsageMode, x => x.EditingUsageRole, x => x.EditingUsageSource)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                this.RaisePropertyChanged(nameof(IsFormPackaging));
                this.RaisePropertyChanged(nameof(IsFormEditingPackaging));
                RecomputeUsageForm();
            });

        this.WhenAnyValue(x => x.EditingUsageSource)
            .Skip(1)
            .Where(_ => !IsBusyApplying && IsUsageFormOpen && IsAddingUsage)
            .Select(source => Observable.FromAsync(cancellationToken => LoadUsageSourceAsync(source, cancellationToken)))
            .Switch()
            .Subscribe();

        // Typing one figure of the piece size works out the other, and makes it the one that's kept.
        this.WhenAnyValue(x => x.EditingUsageWeightPerPiece)
            .Skip(1)
            .Where(_ => !_syncingPieceSize && !IsBusyApplying)
            .Subscribe(_ => PieceSizeTyped(UsageAuthoredField.WeightPerPiece));

        this.WhenAnyValue(x => x.EditingUsageYield)
            .Skip(1)
            .Where(_ => !_syncingPieceSize && !IsBusyApplying)
            .Subscribe(_ => PieceSizeTyped(UsageAuthoredField.Yield));

        // Shrinkage changes what the batch makes, so the figure the user didn't type follows it; which one
        // they typed doesn't change.
        this.WhenAnyValue(x => x.EditingUsageShrinkagePercent)
            .Skip(1)
            .Where(_ => !_syncingPieceSize && !IsBusyApplying)
            .Subscribe(_ => ResyncPieceSize());

        // A packaging line picked, typed or deleted in its table: it's priced, and the product's costs follow.
        LineEdits.ToObservableChangeSet()
            .AutoRefresh(l => l.PackagingMaterialId)
            .AutoRefresh(l => l.CostPerUnit)
            .Subscribe(_ =>
            {
                if (IsRestoring)
                {
                    return;
                }

                PackagingLineRow.Resolve(LineEdits, PackagingMaterials.ToDictionary(m => m.Id));
                RecomputeCosts();
            });

        this.WhenAnyValue(
                x => x.EditingLaborPercent,
                x => x.EditingOverheadPercent,
                x => x.EditingAdministrativePercent,
                x => x.EditingMarginPercent,
                x => x.EditingVatPercent,
                x => x.EditingCurrentPrice,
                x => x.EditingQuantityRequired)
            .Where(_ => !IsRestoring)
            .Subscribe(_ => RecomputeCosts());

        // The caption on every figure names what it's for, so it follows the quantity.
        this.WhenAnyValue(x => x.EditingQuantityRequired)
            .Subscribe(_ => this.RaisePropertyChanged(nameof(CostUnitCaption)));

        UndoCommand = ReactiveCommand.Create(() => { TryUndo(); }, History.WhenAnyValue(h => h.CanUndo));
        RedoCommand = ReactiveCommand.Create(() => { TryRedo(); }, History.WhenAnyValue(h => h.CanRedo));

        // Save offers itself only when the form holds something the saved product doesn't. Re-read on every
        // change rather than only when an undo step settles: a step closes on the way out of a field, and a Save
        // button still disabled under the cursor would swallow the very click that enabled it.
        this.Changed.Subscribe(_ => History.RefreshDirty());
        WatchForSave(UsageEdits);
        WatchForSave(LineEdits);
        WatchForSave(FormLines);
        WatchForSave(FormAddIns);
        WatchForSave(FormPackagingLines);
        ((INotifyPropertyChanged)RecipeTabs).PropertyChanged += (_, _) => RebaseSettledForm();

        // Runs whenever the tab is shown (MainWindow) and after this screen's own saves. It merges
        // rather than replaces, so the screen keeps its selection, pickers and unsaved edits while
        // picking up what changed elsewhere: a repriced ingredient, or a linked sub-assembly its owner
        // changed.
        LoadCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var roles = await subAssemblyRoleRepository.GetAllAsync(CancellationToken.None);
            var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
            var rawMaterials = await rawMaterialRepository.GetAllAsync(CancellationToken.None);
            var products = await _productRepository.GetAllAsync(CancellationToken.None);
            var linkable = await _subAssemblyRepository.GetLinkableAsync(CancellationToken.None);
            var packagingSets = (await _subAssemblyRepository.GetStandaloneAsync(CancellationToken.None)).Where(s => s.Role?.IsPackaging == true).ToList();
            var costingDefaults = await _costingDefaultsRepository.GetAsync(CancellationToken.None);

            // A linked section reads a sub-assembly kept on the Sub-Assemblies screen, which can change there (its
            // lines, name or role), so those are fetched again; the product's own and new sections only need their
            // ingredients' new costs. Every saved one is fetched: a role made product-owned, or no longer, in Settings
            // turns a linked one into the product's own, or back.
            var detailProductId = _detailProductId;
            HashSet<int> linkedIds = [.. UsageEdits.Where(u => u.SubAssemblyId > 0).Select(u => u.SubAssemblyId)];
            var linkedSubAssemblies = await _subAssemblyRepository.GetByIdsAsync(linkedIds, CancellationToken.None);
            var linkedLines = await _subAssemblyLineRepository.GetForSubAssembliesAsync(linkedIds, CancellationToken.None);
            var packagingLink = detailProductId is { } linkProductId
                ? await _packagingLinkRepository.GetForProductAsync(linkProductId, CancellationToken.None)
                : null;

            // Linked packaging is kept elsewhere (another product, or a set on the Sub-Assemblies screen) and can
            // have changed there, so it's fetched again — but only while the form still shows the link as saved. The
            // product's own lines aren't, since they may hold unsaved edits, and nor is a link picked but not saved.
            var savedLink = packagingLink is null ? null : ToLinkTarget(packagingLink);
            var showsSavedLink = savedLink is not null && savedLink.Key == _savedPackagingLinkKey && _packagingLinkTarget?.Key == savedLink.Key;
            var linkedPackagingLines = showsSavedLink
                ? await _packagingLineRepository.GetEffectiveForProductAsync(packagingLink!.ProductId, CancellationToken.None)
                : null;

            // A role made product-owned in Settings, or no longer, since the product was loaded: a section it linked is its
            // own now, or one of its own is linked now. With nothing typed into the form, the product is read again, so what
            // Save compares against is how it's saved now; with something typed, RefreshRecipeRows turns the section round
            // in place, and the next Save writes it as it shows.
            var ownershipChanged = UsageEdits.Any(u => linkedSubAssemblies.FirstOrDefault(s => s.Id == u.SubAssemblyId) is { } saved
                && (saved.OwnerProductId is not null && saved.OwnerProductId == detailProductId) != u.IsOwned);
            var reloadDetails = ownershipChanged && !History.IsDirty && detailProductId is not null;

            // Ids first: a picker whose item gets replaced can clear its own selection mid-merge.
            var selectedId = SelectedProduct?.Id;
            var categoryId = EditingCategory?.Id;
            var usageRoleId = EditingUsageRole?.Id;
            var usageSourceKey = EditingUsageSource?.Key;

            ApplyRefresh(() =>
            {
                Roles.MergeFrom(roles, r => r.Id);
                _categoryCache.MergeFrom(categories);
                EditingCategory = Categories.FirstOrDefault(c => c.Id == categoryId);
                Ingredients.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Ingredient).OrderBy(m => m.Name)], m => m.Id);
                PackagingMaterials.MergeFrom([.. rawMaterials.Where(m => m.Type == RawMaterialType.Packaging).OrderBy(m => m.Name)], m => m.Id);
                _productCache.MergeFrom(products);
                _linkable = linkable;
                _packagingSets = packagingSets;
                RefreshCostingDefaults(costingDefaults);

                SelectedProduct = Products.FirstOrDefault(p => p.Id == selectedId);
                if (SelectedProduct is not null)
                {
                    ShowUpdatedAt(SelectedProduct.UpdatedAt);
                }

                if (detailProductId == _detailProductId && linkedPackagingLines is not null)
                {
                    // A renamed product or set reads right in the banner too.
                    ShowPackagingLink(savedLink);
                    ReplaceLines(linkedPackagingLines);
                }

                RefreshRecipeRows(linkedSubAssemblies, linkedLines);
                RefreshFormLines();
                RefreshLinkableOptions();

                // An add form opened before the roles were loaded takes the first role it can have, as it would have.
                EditingUsageRole = FormRoleOptions.FirstOrDefault(r => r.Id == usageRoleId)
                    ?? (usageRoleId is null && IsUsageFormOpen && IsAddingUsage ? FormRoleOptions.FirstOrDefault() : null);
                EditingUsageSource = LinkableSubAssemblies.FirstOrDefault(o => o.Key == usageSourceKey);
                PackagingLineRow.Resolve(FormPackagingLines, PackagingMaterials.ToDictionary(m => m.Id));

                RecomputeUsageForm();
                RecomputeCosts();
            });

            if (reloadDetails && SelectedProduct is { } reloaded && reloaded.Id == detailProductId)
            {
                await LoadProductDetailAsync(reloaded, CancellationToken.None);
            }

            // Outside ApplyRefresh, whose selection changes only re-point: this one has to load the product's form.
            if (_productToOpen is { } productId)
            {
                _productToOpen = null;
                TrySelectProduct(productId);
            }
        });

        NewCommand = ReactiveCommand.Create(StartNewProduct);

        OpenSubAssemblyCommand = ReactiveCommand.Create<int>(_subAssemblyOpenRequests.OnNext);

        SaveCommand = ReactiveCommand.CreateFromTask(() => RunSafelyAsync(async () =>
        {
            // The picked product's details haven't replaced the previous one's yet: saving now would write that
            // recipe under this product's name. The moment passes in a fraction of a second.
            if (!AreDetailsReady)
            {
                return;
            }

            // A form left open is part of what's being saved, unless it's an add form nothing was entered in, which
            // has nothing to save. Its sub-assembly's tab shows, as Apply would show it, so an add made this way
            // stays the tab showing through the reload.
            if (IsUsageFormOpen && !IsUsageFormUntouched())
            {
                SelectRecipeTab(ApplyForm());
            }

            var product = new Product
            {
                Id = _editingId,
                Name = EditingName,
                Description = string.IsNullOrWhiteSpace(EditingDescription) ? null : EditingDescription.Trim(),
                CategoryId = EditingCategory?.Id,
                QuantityRequired = EditingQuantityRequired,
                CurrentPrice = EditingCurrentPrice,
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
                ChangedPackagingLink(),
                ChangedLines(),
                CurrentCosting() == _savedCosting ? null : costing,
                CancellationToken.None);

            // What's saved is the new starting point: stepping back past it would show changes already written.
            // Save's own reload keeps the product selected, so ClearDetails doesn't run and wouldn't clear this.
            History.Clear();

            // The list picks up the new name; then the form reloads what was saved, which is what the
            // next Save compares against.
            await LoadCommand.Execute();
            await ReloadSavedProductAsync(id);
        }),
        History.WhenAnyValue(h => h.IsDirty));

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

        // The Category picker's "< Add New .. >" row: names a category, saves it, and picks it for the product, so a
        // product can go in a new category without a trip to Settings. Save saves the product's choice.
        AddCategoryCommand = ReactiveCommand.CreateFromTask(async () =>
        {
            var name = await PromptForNewCategory.Handle(("Name for the new category:", CategorySearchText.Trim()));

            if (!string.IsNullOrWhiteSpace(name))
            {
                await RunSafelyAsync(async () =>
                {
                    var category = new ProductCategory { Name = name.Trim() };

                    var validation = _categoryValidator.Validate(category);
                    if (!validation.IsValid)
                    {
                        ErrorMessage = string.Join(" ", validation.Errors.Select(e => e.ErrorMessage));
                        return;
                    }

                    // AddAsync fills in the saved Id, which the reloaded copy to pick is found by. Only the categories are
                    // reloaded: nothing else on the screen changes with a new category.
                    await _categoryRepository.AddAsync(category, CancellationToken.None);
                    var categories = await _categoryRepository.GetAllAsync(CancellationToken.None);
                    ApplyRefresh(() => _categoryCache.MergeFrom(categories));
                    EditingCategory = Categories.FirstOrDefault(c => c.Id == category.Id);
                    SuccessMessage = $"{category.Name} was added.";
                });
            }

            // The picker is still showing the row that was picked, so the category goes back into it: the new one, or
            // the one it had when the prompt was cancelled or the name refused (as RawMaterialsViewModel does).
            this.RaisePropertyChanged(nameof(EditingCategory));
        });

        // Revert reads the picked product's detail again, which replaces the recipe, packaging, packs and costing
        // wholesale, so anything staged but unsaved goes: a section added or edited in the form included, since the
        // form has no discard of its own. A new product was never saved, so it goes back to being a blank one.
        RevertCommand = ReactiveCommand.CreateFromTask(() => RunWithConfirmationAsync(
            "Undo every change made since this product was last saved?",
            async () =>
            {
                if (SelectedProduct is not { } product)
                {
                    StartNewProduct();
                    History.Clear();
                    return;
                }

                History.Clear();
                CloseUsageForm();
                SelectedLineEdit = null;
                LoadIntoForm(product);
                await LoadProductDetailAsync(product, CancellationToken.None);
                SuccessMessage = $"{product.Name} was put back as it was last saved.";
            }));

        DeleteCommand = ReactiveCommand.CreateFromTask(
            () => RunWithConfirmationAsync(
                $"Delete '{SelectedProduct!.Name}'? Its own sub-assemblies go with it; the ones it links stay on the Sub-Assemblies screen. This can't be undone.",
                async () =>
                {
                    await _productRepository.DeleteAsync(SelectedProduct!.Id, CancellationToken.None);
                    StartNewProduct();
                    await LoadCommand.Execute();
                }),
            this.WhenAnyValue(x => x.SelectedProduct).Select(product => product is not null));

        // Add Sub-Assembly opens a New Sub-Assembly tab, after the sub-assemblies' and before Packaging's, with a fresh
        // add form in it, which also adds the packaging (the Packaging role, or a packaging source to link or copy). The form
        // showing is applied first, as leaving its tab would apply it; one a rule refuses keeps its tab, with the
        // message, and nothing opens. The tab goes again once another shows (RecipeTabShown).
        //
        // Its parameter is the way in, one per item on the drop-down. The tab is shown first and the mode set after:
        // showing it starts the form in New (RecipeTabShown), and the mode landing on it starts the form again for a
        // Link or a Copy (the EditingUsageMode subscription), so one path serves all three, in one undo step.
        //
        // The button stays live on the add tab, since the drop-down is the only place the way in can be changed now
        // that the form has no picker of its own. An item picked over an add form nothing was entered in only changes
        // its way, keeping the tab; over one already typed into it does what a second Add Sub-Assembly always did —
        // applies what's there and starts another, in the way picked.
        AddUsageCommand = ReactiveCommand.Create<UsageFormMode>(
            mode =>
            {
                if (SelectedRecipeTab is AddSubAssemblyRecipeTab && IsUsageFormUntouched())
                {
                    AsUndoStep(() => EditingUsageMode = mode);
                    return;
                }

                if (!CommitUsageFormOnLeave())
                {
                    return;
                }

                AsUndoStep(() =>
                {
                    if (!RecipeTabs.Contains(AddSubAssemblyTab))
                    {
                        RecipeTabs.Insert(UsageEdits.Count, AddSubAssemblyTab);
                    }

                    SelectRecipeTab(AddSubAssemblyTab);
                    EditingUsageMode = mode;
                });
            });

        // A sub-assembly's tab is its form, so editing one is just showing its tab (RecipeTabShown opens the form).
        EditUsageCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => SelectRecipeTab(TabFor(usage)));

        // A linked sub-assembly becomes a new section in the same role and at the same piece size, whose lines the form
        // edits; Save keeps it on the Sub-Assemblies screen as a sub-assembly of its own, and links that instead.
        MakeCopyCommand = ReactiveCommand.Create<ProductUsageEdit>(usage => AsUndoStep(() =>
        {
            // Its add-ins become plain lines of the copy: the batch it was showing is the batch it keeps.
            // Never a duplicate ingredient, since an add-in can't be one the recipe already has.
            // In a role the recipe has more than one in, it keeps what it went by, so it's still told apart.
            var copy = new ProductUsageEdit(
                0, usage.Role, [.. usage.Lines, .. usage.AddIns], usage.AuthoredField, usage.AuthoredValue, usage.ShrinkagePercent,
                linkedName: null, name: usage.IsRoleRepeated ? usage.Label : null);
            ReplaceUsage(usage, copy);
        }));

        // Delete Sub-Assembly takes out the one whose tab is showing. Nothing to confirm: it only leaves the recipe,
        // with Save, and the sub-assembly stays on the Sub-Assemblies screen. On the Packaging tab it takes out the
        // packaging: its lines, or its link, go with Save.
        RemoveUsageCommand = ReactiveCommand.CreateFromTask(
            () => RunSafelyAsync(() =>
            {
                if (SelectedRecipeTab is PackagingRecipeTab)
                {
                    AsUndoStep(RemovePackaging);
                    return Task.CompletedTask;
                }

                var usage = ((SubAssemblyRecipeTab)SelectedRecipeTab!).Usage;
                AsUndoStep(() =>
                {
                    // Whatever was typed into its form goes with it, rather than being applied on the way out.
                    if (ReferenceEquals(usage, EditingUsage))
                    {
                        CloseUsageForm();
                    }

                    // Its neighbour's tab shows instead: the next sub-assembly's, else the one before, else Packaging.
                    var index = UsageEdits.IndexOf(usage);
                    var neighbour = index + 1 < UsageEdits.Count ? UsageEdits[index + 1] : index > 0 ? UsageEdits[index - 1] : null;
                    SelectRecipeTab(neighbour is null ? PackagingTabIfShown : TabFor(neighbour));

                    UsageEdits.Remove(usage);
                    RefreshLinkableOptions();
                    RecomputeCosts();
                });

                return Task.CompletedTask;
            }),
            this.WhenAnyValue(x => x.SelectedRecipeTab).Select(tab => tab is SubAssemblyRecipeTab or PackagingRecipeTab));

        RemoveFormLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => FormLines.Remove(SelectedFormLine!)),
            this.WhenAnyValue(x => x.SelectedFormLine).Select(line => line is not null));

        ToggleFormAddInsCommand = ReactiveCommand.Create(() => { IsFormAddInsExpanded = !IsFormAddInsExpanded; });

        RemoveFormPackagingLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => FormPackagingLines.Remove(SelectedFormPackagingLine!)),
            this.WhenAnyValue(x => x.SelectedFormPackagingLine).Select(line => line is not null));

        // The linked packaging's lines, as they show, become the product's own; Save drops the link and writes them.
        CopyPackagingCommand = ReactiveCommand.Create(
            () => AsUndoStep(() =>
            {
                ShowPackagingLink(null);
                RefreshLinkableOptions();
            }),
            this.WhenAnyValue(x => x.IsPackagingLinked));

        RemoveFormAddInCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => FormAddIns.Remove(SelectedFormAddIn!)),
            this.WhenAnyValue(x => x.SelectedFormAddIn).Select(line => line is not null));

        RemoveLineCommand = ReactiveCommand.Create(
            () => AsUndoStep(() => LineEdits.Remove(SelectedLineEdit!)),
            this.WhenAnyValue(x => x.SelectedLineEdit, x => x.IsPackagingLinked, (line, linked) => line is not null && !linked));

        // A clone copies the product as saved, and a variant links it as saved: unsaved edits stay with this one.
        var canMakeFromSelected = this.WhenAnyValue(
            x => x.SelectedProduct, x => x.EditingCloneName, (product, name) => product is not null && !string.IsNullOrWhiteSpace(name));
        CloneCommand = ReactiveCommand.CreateFromTask(() => MakeFromSelectedAsync(_productCloner.CloneAsync), canMakeFromSelected);
        CreateVariantCommand = ReactiveCommand.CreateFromTask(() => MakeFromSelectedAsync(_productCloner.CreateVariantAsync), canMakeFromSelected);
    }

    /// <summary>Makes a new product named EditingCloneName from the selected one as saved (a clone or a variant), then shows it.</summary>
    private Task MakeFromSelectedAsync(Func<int, string, CancellationToken, Task<int>> make) =>
        RunSafelyAsync(async () =>
        {
            var newId = await make(SelectedProduct!.Id, EditingCloneName, CancellationToken.None);
            await LoadCommand.Execute();
            SelectedProduct = Products.FirstOrDefault(p => p.Id == newId);
        });

    /// <summary>The Last Updated figure: the day and the time; "—" for a product never saved since it was added.</summary>
    private void ShowUpdatedAt(DateTime? updatedAt) =>
        LastUpdatedText = updatedAt?.ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture) ?? "—";

    private void LoadIntoForm(Product product)
    {
        IsDescriptionEditorOpen = false;
        _editingId = product.Id;
        EditingName = product.Name;
        EditingDescription = product.Description ?? string.Empty;
        EditingCategory = Categories.FirstOrDefault(c => c.Id == product.CategoryId);
        EditingQuantityRequired = product.QuantityRequired;
        EditingCurrentPrice = product.CurrentPrice;
        ShowUpdatedAt(product.UpdatedAt);
        EditingCloneName = $"{product.Name} Copy";
    }

    /// <summary>
    /// What follows a new selection: nothing picked or open. Another product's details wait for its own to
    /// arrive and go with them (AreDetailsReady); the same product picked again (a fresh copy after Save) keeps
    /// showing until its reload, and none picked clears them.
    /// </summary>
    private void ResetDetails(Product? product)
    {
        // Another product's details replace these when they arrive, in the same pass (ClearDetailsForAnother).
        // Cleared here, the screen drew an empty product in between: a flicker, with the recipe's frame
        // collapsing and growing back.
        if (product is not null && product.Id != _detailProductId)
        {
            AreDetailsReady = false;
            return;
        }

        AreDetailsReady = true;
        CloseUsageForm();
        SelectedLineEdit = null;

        if (product?.Id != _detailProductId)
        {
            ClearDetails();
        }
    }

    /// <summary>
    /// What ResetDetails leaves for the picked product's details to replace: the previous product's. Its recipe's tabs
    /// stay for the picked recipe's sections to take over (ReplaceUsages), unless nothing is coming to replace them.
    /// </summary>
    private void ClearDetailsForAnother(bool keepRecipeTabs)
    {
        CloseUsageForm();
        SelectedLineEdit = null;
        ClearDetails(keepRecipeTabs);
    }

    /// <summary>
    /// Opens a product as picking it in the list would, for another screen (Sub-Assemblies' Used By tiles). A search
    /// hiding it is cleared, so the list shows the row picked. A list that doesn't hold it yet (the screen not loaded
    /// since, or ever) opens it at the end of the next LoadCommand, which showing the screen runs.
    /// </summary>
    public void OpenProduct(int productId)
    {
        _productToOpen = TrySelectProduct(productId) ? null : productId;
    }

    private bool TrySelectProduct(int productId)
    {
        if (Products.FirstOrDefault(p => p.Id == productId) is not { } product)
        {
            return false;
        }

        if (!VisibleProducts.Contains(product))
        {
            SearchText = string.Empty;
        }

        SelectedProduct = product;
        return true;
    }

    /// <summary>A new product: no name, recipe, packaging or links, its costing at the defaults, and nothing selected.</summary>
    private void StartNewProduct()
    {
        SelectedProduct = null;
        _editingId = 0;
        EditingName = string.Empty;
        EditingDescription = string.Empty;
        EditingCategory = null;
        EditingQuantityRequired = 1;
        EditingCurrentPrice = null;
        IsDescriptionEditorOpen = false;
        ShowUpdatedAt(null);
        EditingCloneName = string.Empty;

        // After an unsaved new product nothing was selected, so the selection subscriptions don't run.
        CloseUsageForm();
        SelectedLineEdit = null;
        ClearDetails();
        ApplyCostingDefaults();

        // The blank form is what Save's dirty check measures against, so nothing is offered until it's filled in.
        History.MarkClean();
    }

    /// <summary>
    /// Empties the recipe, packaging, costing and links, and what Save compares them against. With
    /// <paramref name="keepRecipeTabs"/>, the recipe's sections and the Packaging tab stay for another product's to
    /// replace in the same pass: the tab strip then keeps its tabs rather than building a new one for each.
    /// </summary>
    private void ClearDetails(bool keepRecipeTabs = false)
    {
        // The history is this product's form; another product's steps would put back a recipe that isn't its.
        History.Clear();

        var previousTab = SelectedRecipeTab;
        _detailProductId = null;

        _arrangingRecipeTabs = true;
        try
        {
            if (!keepRecipeTabs)
            {
                UsageEdits.Clear();
            }

            RecipeTabs.Remove(AddSubAssemblyTab);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        LineEdits.Clear();
        if (!keepRecipeTabs)
        {
            SetHasPackaging(false);
        }
        SetPackagingLink(null);
        _savedRecipe = new RecipeDraft([], []);
        _savedLines = [];
        SetCosting(null);
        RefreshLinkableOptions();
        RecomputeCosts();

        // A picked product's sub-assemblies are on their way, and pick the tab when they arrive.
        _isRecipeClearedForAnother = SelectedProduct is not null;
        if (SelectedProduct is null)
        {
            ReselectRecipeTab(previousTab, previousUsage: null, previousIds: []);
        }
    }

    private async Task LoadProductDetailAsync(Product? product, CancellationToken cancellationToken)
    {
        if (product is null)
        {
            // Nothing picked: the form is a blank one, and there's nothing to save until it's typed into.
            History.MarkClean();
            return;
        }

        // Read on the thread pool, so the UI thread comes back once, when all of it has arrived, instead of
        // after each read to start the next. The reads touch no ViewModel state; everything below them does.
        ProductDetail detail;
        try
        {
            detail = await Task.Run(() => ReadProductDetailAsync(product.Id, cancellationToken), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested && product.Id != _detailProductId)
        {
            // Nothing of the picked product's came: the previous product's mustn't stay showing under its name.
            ClearDetailsForAnother(keepRecipeTabs: false);
            AreDetailsReady = true;
            throw;
        }

        // Only the product picked last is shown; a newer pick cancels this load.
        cancellationToken.ThrowIfCancellationRequested();

        // Out with the previous product's details and in with these, in one pass, so nothing is drawn between them. Its
        // recipe's tabs stay, for this recipe's sections to take over.
        if (product.Id != _detailProductId)
        {
            ClearDetailsForAnother(keepRecipeTabs: true);
        }

        AreDetailsReady = true;
        _detailProductId = product.Id;

        // The packaging first, so the recipe's tabs, rebuilt next, can reselect the Packaging tab if it showed.
        SetPackagingLink(detail.PackagingLink);
        ReplaceLines(detail.Lines);
        SetHasPackaging(detail.PackagingLink is not null || detail.Lines.Count > 0);
        ReplaceUsages(detail.Usages, detail.SubAssemblyLines, detail.AddIns);
        SetCosting(detail.Costing);
        RefreshLinkableOptions();
        RecomputeCosts();
        History.MarkClean();
    }

    /// <summary>Everything a product's detail shows, as LoadProductDetailAsync reads it.</summary>
    private sealed record ProductDetail(
        PackagingLink? PackagingLink,
        IReadOnlyList<ProductSubAssemblyUsage> Usages,
        IReadOnlyList<PackagingLine> Lines,
        CostingParameters? Costing,
        IReadOnlyList<SubAssemblyLine> SubAssemblyLines,
        IReadOnlyList<UsageAddInLine> AddIns);

    /// <summary>Only the repositories: this runs off the UI thread.</summary>
    private async Task<ProductDetail> ReadProductDetailAsync(int productId, CancellationToken cancellationToken)
    {
        var packagingLink = await _packagingLinkRepository.GetForProductAsync(productId, cancellationToken);
        var usages = await _usageRepository.GetForProductAsync(productId, cancellationToken);
        var lines = await _packagingLineRepository.GetEffectiveForProductAsync(productId, cancellationToken);
        var costing = await _costingParametersRepository.GetForProductAsync(productId, cancellationToken);
        var subAssemblyLines = await _subAssemblyLineRepository.GetForSubAssembliesAsync(
            [.. usages.Select(u => u.SubAssemblyId).Distinct()], cancellationToken);
        var addIns = await _usageRepository.GetAddInsForProductAsync(productId, cancellationToken);
        return new ProductDetail(packagingLink, usages, lines, costing, subAssemblyLines, addIns);
    }

    /// <summary>The saved packaging link, or none: what the Packaging tab shows, and what Save compares against.</summary>
    private void SetPackagingLink(PackagingLink? link)
    {
        var target = link is null ? null : ToLinkTarget(link);
        _savedPackagingLinkKey = target?.Key;
        ShowPackagingLink(target);
    }

    /// <summary>What the per-piece packaging links as the form stands, saved or not, and the Packaging tab's banner.</summary>
    private void ShowPackagingLink(PackagingLinkTarget? target)
    {
        _packagingLinkTarget = target;
        IsPackagingLinked = target is not null;
        PackagingLinkedToText = target?.LinkedToText ?? string.Empty;
        this.RaisePropertyChanged(nameof(PackagingLinkedSetId));
        this.RaisePropertyChanged(nameof(IsPackagingLinkedToSet));
        this.RaisePropertyChanged(nameof(IsPackagingLinkedToProduct));
    }

    private static PackagingLinkTarget ToLinkTarget(PackagingLink link) =>
        link.LinkedSubAssemblyId is { } setId
            ? PackagingLinkTarget.ForSet(setId, link.LinkedSubAssembly?.Name ?? string.Empty)
            : PackagingLinkTarget.ForProduct(link.LinkedProductId!.Value, link.LinkedProduct?.Name ?? string.Empty);

    /// <summary>The Packaging tab comes and goes with the packaging, always last, after the add form's tab.</summary>
    private void SetHasPackaging(bool hasPackaging)
    {
        HasPackaging = hasPackaging;
        if (hasPackaging == RecipeTabs.Contains(PackagingTab))
        {
            return;
        }

        _arrangingRecipeTabs = true;
        try
        {
            if (hasPackaging)
            {
                RecipeTabs.Add(PackagingTab);
            }
            else
            {
                RecipeTabs.Remove(PackagingTab);
            }
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }
    }

    /// <summary>The Packaging tab while the product has packaging, else null: the tab to fall back on after the sub-assemblies'.</summary>
    private RecipeTab? PackagingTabIfShown => HasPackaging ? PackagingTab : null;

    /// <summary>
    /// Delete Sub-Assembly on the Packaging tab: the packaging leaves the recipe, lines or link, and its tab goes. The
    /// last sub-assembly's tab shows instead, if there is one. Save writes it; the packs keep their own lines.
    /// </summary>
    private void RemovePackaging()
    {
        SelectRecipeTab(RecipeTabs.OfType<SubAssemblyRecipeTab>().LastOrDefault());
        SelectedLineEdit = null;
        LineEdits.Clear();
        ShowPackagingLink(null);
        SetHasPackaging(false);
        RefreshLinkableOptions();
        RecomputeCosts();
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
        List<ProductUsageEdit> sections =
        [
            .. usages
                .Select(u => ToUsageEdit(u, linesBySubAssembly[u.SubAssemblyId], addInsByUsage[u.Id], ingredientsById))
                .Order(ProductUsageEdit.RecipeOrder),
        ];

        // Another product's recipe opens on its first tab; the same product's, reloaded, keeps the tab it showed. Its
        // section is read now, before the tabs take on the new sections.
        var previousTab = _isRecipeClearedForAnother ? null : SelectedRecipeTab;
        var previousUsage = (previousTab as SubAssemblyRecipeTab)?.Usage;
        HashSet<int> previousIds = [.. UsageEdits.Select(u => u.SubAssemblyId)];
        _isRecipeClearedForAnother = false;

        _arrangingRecipeTabs = true;
        try
        {
            ReplaceUsageItems(sections);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }

        _savedRecipe = CurrentRecipe();
        ReselectRecipeTab(previousTab, previousUsage, previousIds);
    }

    /// <summary>
    /// Follows <paramref name="rows"/> for Save's sake: a row added, removed or replaced can be something to
    /// save. Watched through the collection's own PropertyChanged rather than its CollectionChanged, because an
    /// ObservableCollection only enforces its re-entrancy guard once more than one handler is listening - and
    /// these collections are changed from inside their own notifications, so a second CollectionChanged listener
    /// would turn a change the guard tolerates today into an exception. What's typed into a row reaches Save
    /// another way: the grid closes the cell's undo step, which re-reads whether there's anything to save.
    /// </summary>
    private void WatchForSave(INotifyPropertyChanged rows) => rows.PropertyChanged += (_, _) => History.RefreshDirty();

    void IUndoRecorder.BeginUndoStep() => History.Begin();

    void IUndoRecorder.EndUndoStep() => History.End();

    bool IUndoRecorder.IsEditPending
    {
        get => History.IsEditPending;
        set => History.IsEditPending = value;
    }

    /// <summary>Records <paramref name="change"/> as one undo step, for a command that makes its change in one go.</summary>
    private void AsUndoStep(Action change)
    {
        History.Begin();
        try
        {
            change();
        }
        finally
        {
            History.End();
        }
    }

    /// <summary>Settles whatever step is open, then puts the form back one step. False when there was nothing to undo.</summary>
    public bool TryUndo()
    {
        History.End();
        if (!History.TryUndo(out var restore))
        {
            return false;
        }

        RestoreSnapshot(restore);
        return true;
    }

    /// <summary>Steps forward again into what the last undo stepped off. False when there was nothing to redo.</summary>
    public bool TryRedo()
    {
        History.End();
        if (!History.TryRedo(out var restore))
        {
            return false;
        }

        RestoreSnapshot(restore);
        return true;
    }

    /// <summary>The form as it stands: everything an undo step puts back. See ProductFormSnapshot for what's left out.</summary>
    private ProductFormSnapshot CaptureSnapshot() => new()
    {
        EditingId = _editingId,
        Name = EditingName,
        Description = EditingDescription,
        CurrentPrice = EditingCurrentPrice,
        QuantityRequired = EditingQuantityRequired,
        CategoryId = EditingCategory?.Id,
        CloneName = EditingCloneName,
        Costing = (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent, EditingVatPercent),
        Usages = [.. UsageEdits],
        SelectedTab = CaptureTabChoice(),
        HasAddSubAssemblyTab = RecipeTabs.Contains(AddSubAssemblyTab),
        HasPackaging = HasPackaging,
        PackagingLink = _packagingLinkTarget,
        Lines = [.. LineEdits.Select(CaptureLine)],
        IsUsageFormOpen = IsUsageFormOpen,
        IsAddingUsage = IsAddingUsage,
        UsageFormTitle = UsageFormTitle,
        UsageMode = EditingUsageMode,
        IsFormEditingLines = IsFormEditingLines,
        IsFormPickingSource = IsFormPickingSource,
        IsFormAddInsExpanded = IsFormAddInsExpanded,
        EditingUsageIndex = EditingUsage is { } editing && UsageEdits.IndexOf(editing) is var i and >= 0 ? i : null,
        UsageRoleId = EditingUsageRole?.Id,
        UsageName = EditingUsageName,
        UsageSourceKey = EditingUsageSource?.Key,
        FormLines = [.. FormLines.Select(CaptureIngredientLine)],
        FormAddIns = [.. FormAddIns.Select(CaptureIngredientLine)],
        FormPackagingLines = [.. FormPackagingLines.Select(CaptureLine)],
        FormFormulaBase = _formFormulaBase,
        UsageAuthoredField = _usageAuthoredField,
        UsageWeightPerPiece = EditingUsageWeightPerPiece,
        UsageYield = EditingUsageYield,
        UsageShrinkagePercent = EditingUsageShrinkagePercent,
    };

    private static IngredientLineState CaptureIngredientLine(IngredientLineRow row) =>
        new(row.IngredientId, row.Grams, row.IsFormulaBase, row.AuthoredBakersPercent);

    private static PackagingLineState CaptureLine(PackagingLineRow row) => new(row.PackagingMaterialId, row.UnitId, row.QtyPerUnit);

    private RecipeTabChoice CaptureTabChoice() => SelectedRecipeTab switch
    {
        SubAssemblyRecipeTab tab => new RecipeTabChoice(RecipeTabKind.Usage, RecipeTabs.OfType<SubAssemblyRecipeTab>().ToList().IndexOf(tab)),
        AddSubAssemblyRecipeTab => new RecipeTabChoice(RecipeTabKind.AddSubAssembly, -1),
        PackagingRecipeTab => new RecipeTabChoice(RecipeTabKind.Packaging, -1),
        _ => new RecipeTabChoice(RecipeTabKind.None, -1),
    };

    /// <summary>
    /// Puts the whole form back as <paramref name="snapshot"/> had it. Order matters throughout, and is explained
    /// step by step below; the one recompute runs at the end, outside IsRestoring, so the figures are worked out
    /// once from the restored form rather than over and over from a half-restored one.
    /// </summary>
    private void RestoreSnapshot(ProductFormSnapshot snapshot)
    {
        // A message about a form that no longer exists would outlive what it was about.
        ErrorMessage = null;

        ApplyRestore(() =>
        {
            // 1. The form goes first. EditingUsage has to be null before the sections are replaced: the tab mirror
            //    refills FormLines from a section that replaces the one being edited, which would undo this restore.
            CloseUsageForm();
            SelectedFormLine = null;
            SelectedFormAddIn = null;
            SelectedFormPackagingLine = null;
            SelectedLineEdit = null;

            // 2. The product's own fields. The category goes back by id, into the field: assigning the property
            //    runs the picker's "< Add New .. >" sentinel, which opens a prompt.
            _editingId = snapshot.EditingId;
            EditingName = snapshot.Name;
            EditingDescription = snapshot.Description;
            EditingCurrentPrice = snapshot.CurrentPrice;
            EditingQuantityRequired = snapshot.QuantityRequired;
            EditingCloneName = snapshot.CloneName;
            _editingCategory = snapshot.CategoryId is { } categoryId ? Categories.FirstOrDefault(c => c.Id == categoryId) : null;
            this.RaisePropertyChanged(nameof(EditingCategory));

            // 3. Costing.
            (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent, EditingVatPercent) =
                snapshot.Costing;

            // 4. Packaging. _savedPackagingLinkKey is deliberately left alone: it's what Save compares against.
            ShowPackagingLink(snapshot.PackagingLink);
            LineEdits.Clear();
            foreach (var line in snapshot.Lines)
            {
                LineEdits.Add(RestoreLine(line));
            }

            SetHasPackaging(snapshot.HasPackaging);

            // 5. The sections and their tabs, under _arrangingRecipeTabs so RecipeTabShown short-circuits: without
            //    it, moving the tab back would apply the form the tab is being moved off, which is the very change
            //    being undone.
            _arrangingRecipeTabs = true;
            try
            {
                ReplaceUsageItems(snapshot.Usages);

                if (snapshot.HasAddSubAssemblyTab)
                {
                    if (!RecipeTabs.Contains(AddSubAssemblyTab))
                    {
                        RecipeTabs.Insert(UsageEdits.Count, AddSubAssemblyTab);
                    }
                }
                else
                {
                    RecipeTabs.Remove(AddSubAssemblyTab);
                }

                SelectedRecipeTab = snapshot.SelectedTab switch
                {
                    { Kind: RecipeTabKind.Usage, UsageIndex: var index } =>
                        RecipeTabs.OfType<SubAssemblyRecipeTab>().ElementAtOrDefault(index),
                    { Kind: RecipeTabKind.AddSubAssembly } => RecipeTabs.Contains(AddSubAssemblyTab) ? AddSubAssemblyTab : null,
                    { Kind: RecipeTabKind.Packaging } => PackagingTabIfShown,
                    _ => null,
                };
            }
            finally
            {
                _arrangingRecipeTabs = false;
            }

            // RecipeTabShown never ran, and it's the only other thing that writes these. Left stale, the next real
            // change of tab would think it was leaving a tab that's gone, and apply the wrong form on the way out.
            _shownRecipeTab = SelectedRecipeTab;
            if (SelectedRecipeTab is not null and not AddSubAssemblyRecipeTab)
            {
                _lastRecipeContentTab = SelectedRecipeTab;
            }

            // 6. The form itself, last. The option lists are rebuilt before the pickers are put back, as a load does,
            //    and RefreshLinkableOptions runs inside the guard: outside it, the source it can null would start a
            //    load that clears the very lines being restored.
            IsAddingUsage = snapshot.IsAddingUsage;
            UsageFormTitle = snapshot.UsageFormTitle;
            _editingUsageMode = snapshot.UsageMode;
            this.RaisePropertyChanged(nameof(EditingUsageMode));
            IsFormEditingLines = snapshot.IsFormEditingLines;
            IsFormPickingSource = snapshot.IsFormPickingSource;
            IsFormAddInsExpanded = snapshot.IsFormAddInsExpanded;

            RefreshLinkableOptions();
            RefreshFormRoleOptions();

            _editingUsageRole = snapshot.UsageRoleId is { } roleId ? FormRoleOptions.FirstOrDefault(r => r.Id == roleId) : null;
            this.RaisePropertyChanged(nameof(EditingUsageRole));
            _editingUsageSource = snapshot.UsageSourceKey is { } sourceKey
                ? LinkableSubAssemblies.FirstOrDefault(o => o.Key == sourceKey)
                : null;
            this.RaisePropertyChanged(nameof(EditingUsageSource));
            _editingUsageName = snapshot.UsageName;
            this.RaisePropertyChanged(nameof(EditingUsageName));

            _formFormulaBase = snapshot.FormFormulaBase;
            RestoreIngredientLines(FormLines, snapshot.FormLines, snapshot.FormFormulaBase);
            RestoreIngredientLines(FormAddIns, snapshot.FormAddIns, snapshot.FormFormulaBase);
            FormPackagingLines.Clear();
            foreach (var line in snapshot.FormPackagingLines)
            {
                FormPackagingLines.Add(RestoreLine(line));
            }

            _usageAuthoredField = snapshot.UsageAuthoredField;
            _editingUsageWeightPerPiece = snapshot.UsageWeightPerPiece;
            this.RaisePropertyChanged(nameof(EditingUsageWeightPerPiece));
            _editingUsageYield = snapshot.UsageYield;
            this.RaisePropertyChanged(nameof(EditingUsageYield));
            _editingUsageShrinkagePercent = snapshot.UsageShrinkagePercent;
            this.RaisePropertyChanged(nameof(EditingUsageShrinkagePercent));

            EditingUsage = snapshot.EditingUsageIndex is { } usageIndex && usageIndex < UsageEdits.Count
                ? UsageEdits[usageIndex]
                : null;
            IsUsageFormOpen = snapshot.IsUsageFormOpen;
        });

        // 8. One pass over the restored form, outside the guard. FormLinesChanged is the screen's own "the lines
        //    changed, bring everything into line" entry point: it raises what the form's tables show, works the
        //    derived piece-size figure out again, and runs the single ApplyFormulaBase pass that gives every row
        //    back its BaseWeight and BatchWeight.
        this.RaisePropertyChanged(nameof(IsFormPackaging));
        this.RaisePropertyChanged(nameof(IsFormEditingPackaging));
        FormLinesChanged();
        RecomputeCosts();
    }

    private void RestoreIngredientLines(
        ObservableCollection<IngredientLineRow> rows, IReadOnlyList<IngredientLineState> states, decimal baseWeight)
    {
        rows.Clear();
        foreach (var state in states)
        {
            rows.Add(IngredientLineRow.Restore(
                id => Ingredients.FirstOrDefault(m => m.Id == id),
                baseWeight,
                state.IngredientId,
                state.Grams,
                state.IsFormulaBase,
                state.AuthoredBakersPercent));
        }
    }

    // The material goes on first: it brings the units the row can be in, and a key it doesn't offer falls back
    // to the base unit, which would drop a restored unit set the other way round.
    private PackagingLineRow RestoreLine(PackagingLineState state) =>
        new(FindPackagingMaterial)
        {
            PackagingMaterialId = state.PackagingMaterialId,
            UnitKey = state.UnitId ?? PackagingUnitChoice.BaseUnitKey,
            QtyPerUnit = state.QtyPerUnit,
        };

    /// <summary>
    /// Makes UsageEdits hold <paramref name="sections"/>, section for section and in place: each tab stays and shows its
    /// new section (MirrorUsageTabs), so the tab strip keeps the tabs it has and only adds or takes away the difference,
    /// rather than building a new TabItemExt, template and all, for every section of every product picked. Never Clear(),
    /// which the mirror reads as every tab going. Call it with _arrangingRecipeTabs set, so the strip's own pick as a tab
    /// goes is ignored. It leaves _savedRecipe alone, which is why an undo can use it and ReplaceUsages can't be reused.
    /// </summary>
    private void ReplaceUsageItems(IReadOnlyList<ProductUsageEdit> sections)
    {
        for (var i = 0; i < sections.Count; i++)
        {
            if (i < UsageEdits.Count)
            {
                UsageEdits[i] = sections[i];
            }
            else
            {
                UsageEdits.Add(sections[i]);
            }
        }

        while (UsageEdits.Count > sections.Count)
        {
            UsageEdits.RemoveAt(UsageEdits.Count - 1);
        }
    }

    private ProductUsageEdit ToUsageEdit(
        ProductSubAssemblyUsage usage,
        IEnumerable<SubAssemblyLine> lines,
        IEnumerable<UsageAddInLine> addIns,
        IReadOnlyDictionary<int, RawMaterial> ingredientsById)
    {
        var subAssembly = usage.SubAssembly!;
        var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
        var isOwn = subAssembly.OwnerProductId == usage.ProductId;
        return new ProductUsageEdit(
            subAssembly.Id,
            role,
            [.. lines.Select(l => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
            usage.AuthoredField,
            usage.AuthoredValue,
            usage.ShrinkagePercent,
            isOwn ? null : subAssembly.Name,
            [.. addIns.Select(a => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(a.IngredientId) ?? a.Ingredient!, a.Grams))],
            SectionLabelOf(subAssembly.Name, role));
    }

    /// <summary>
    /// What a linked sub-assembly goes by beside its role in this product's recipe: its name, less the product's own and
    /// the role it was saved under from here ("Ube" for "Cheese Roll Filling: Ube"), as SetRecipeAsync reads it.
    /// </summary>
    private string? SectionLabelOf(string subAssemblyName, SubAssemblyRole? role) =>
        SubAssemblyRules.SectionLabel(subAssemblyName, EditingName, role?.Name ?? string.Empty);

    /// <summary>The saved packaging lines in the Packaging tab's table; they're also what Save compares against.</summary>
    private void ReplaceLines(IReadOnlyList<PackagingLine> lines)
    {
        ShowPackagingLines(lines.Select(ToPackagingRow));
        _savedLines = CurrentLines();
    }

    /// <summary>Puts <paramref name="rows"/> in the Packaging tab's table, in place of whatever it held.</summary>
    private void ShowPackagingLines(IEnumerable<PackagingLineRow> rows)
    {
        SelectedLineEdit = null;
        LineEdits.Clear();
        LineEdits.AddRange(rows);
    }

    /// <summary>
    /// A packaging line as a table row, its quantity in the unit it was entered in. A line saved as pieces per unit
    /// shows as the quantity it comes to, at the same cost, and is saved that way once the packaging changes.
    /// </summary>
    private static PackagingLineRow ToPackagingRow(PackagingLine line)
    {
        var authoredValue = line.AuthoredField == PackagingLineAuthoredField.QtyPerUnit ? line.QtyPerUnit : line.UnitsPerPackagingUnit;
        var qtyPerUnit = ProductRules.ReconcileReciprocal(line.AuthoredField, authoredValue).QtyPerUnit;
        return new PackagingLineRow(line.PackagingMaterial!, qtyPerUnit, line.UnitId);
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

    /// <summary>
    /// A new product's costing starts at the defaults, while what Save compares against stays at none (SetCosting(null)),
    /// so its first Save writes them as its own. Defaults of all zero write nothing, as before there were defaults.
    /// </summary>
    private void ApplyCostingDefaults() =>
        (EditingLaborPercent, EditingOverheadPercent, EditingAdministrativePercent, EditingMarginPercent, EditingVatPercent) = _costingDefaults;

    /// <summary>
    /// Takes up the defaults as LoadCommand read them. A new product still showing the ones it started with (the screen's
    /// first load, or defaults changed in Settings since) takes the new ones; one whose costing was typed into keeps it.
    /// </summary>
    private void RefreshCostingDefaults(CostingDefaults defaults)
    {
        var untouchedNewProduct = _editingId == 0 && CurrentCosting() == _costingDefaults;
        _costingDefaults = (defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent, defaults.VatPercent);
        if (untouchedNewProduct)
        {
            ApplyCostingDefaults();

            // The defaults landing on a form that was still showing the previous ones isn't the user's change -
            // on the screen's first load they land on a blank form costed at nothing - so what Save measures
            // against follows them. Only the costing moves: a product whose costing was typed into isn't here.
            History.Rebase((settled, now) => settled with { Costing = now.Costing });
        }
    }

    /// <summary>The packaging lines as Save writes them, each in the unit it was typed in; a line half filled in is refused (PackagingLineRow.CompleteLines).</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> CurrentLines() =>
        [.. PackagingLineRow.CompleteLines(LineEdits).Select(l => (l.PackagingMaterial.Id, l.UnitId, PackagingLineAuthoredField.QtyPerUnit, l.QtyPerUnit))];

    /// <summary>The recipe as Save writes it: the new sections with their lines, and the sub-assemblies it links.</summary>
    private RecipeDraft CurrentRecipe() =>
        new(
            [.. UsageEdits.Where(u => u.IsEditable).Select(u => new NewSubAssemblyDraft(
                u.RoleId, [.. u.Lines.Select(l => new RecipeLine(l.Ingredient.Id, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], u.AuthoredField, u.AuthoredValue, u.Name)
            {
                // The product's own keeps its id, so Save rewrites it in place; a new section's is 0.
                Id = u.SubAssemblyId,
                ShrinkagePercent = u.ShrinkagePercent,
            })],
            [.. UsageEdits.Where(u => u.IsLinked).Select(u => new LinkedUsageDraft(u.SubAssemblyId, u.AuthoredField, u.AuthoredValue)
            {
                ShrinkagePercent = u.ShrinkagePercent,
                AddIns = [.. u.AddIns.Select(a => (a.Ingredient.Id, a.Grams))],
            })]);

    /// <summary>
    /// Whether two recipes would save the same: the same sub-assemblies, roles, lines, add-ins and piece
    /// sizes, in the same order. A linked draft is compared field by field rather than as a record,
    /// as a new one already is: a record's own Equals compares its AddIns list by reference, which no
    /// two recipes built separately ever share, so Save would write the recipe every time.
    /// </summary>
    private static bool IsSameRecipe(RecipeDraft a, RecipeDraft b) =>
        a.Linked.Count == b.Linked.Count
        && a.Linked.Zip(b.Linked).All(pair =>
            pair.First.SubAssemblyId == pair.Second.SubAssemblyId
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.ShrinkagePercent == pair.Second.ShrinkagePercent
            && pair.First.AddIns.SequenceEqual(pair.Second.AddIns))
        && a.New.Count == b.New.Count
        && a.New.Zip(b.New).All(pair =>
            pair.First.Id == pair.Second.Id
            && pair.First.RoleId == pair.Second.RoleId
            && pair.First.SectionName == pair.Second.SectionName
            && pair.First.AuthoredField == pair.Second.AuthoredField
            && pair.First.AuthoredValue == pair.Second.AuthoredValue
            && pair.First.ShrinkagePercent == pair.Second.ShrinkagePercent
            && pair.First.Lines.SequenceEqual(pair.Second.Lines));

    /// <summary>The packaging lines when they differ from those last loaded, else null. Linked packaging is never saved here.</summary>
    private List<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? ChangedLines()
    {
        if (IsPackagingLinked)
        {
            return null;
        }

        // Packaging that was linked when loaded, and is the product's own now (Make a Copy, or a new or copied one in its
        // place), is written even when its lines match what the link showed: the product had none of its own.
        var current = CurrentLines();
        return current.SequenceEqual(_savedLines) && _savedPackagingLinkKey is null ? null : current;
    }

    /// <summary>What the packaging links when it differs from the link last loaded, else null; no link is PackagingLinkDraft.None.</summary>
    private PackagingLinkDraft? ChangedPackagingLink() =>
        _packagingLinkTarget?.Key == _savedPackagingLinkKey
            ? null
            : _packagingLinkTarget is { } target ? new PackagingLinkDraft(target.ProductId, target.SubAssemblyId) : PackagingLinkDraft.None;

    /// <summary>
    /// Loads the saved product again and selects the fresh copy, so the form shows (and compares against) what was
    /// saved. A new product, saved for the first time, is still the recipe showing, not another product's: it keeps
    /// the tab it showed, as an existing one does.
    /// </summary>
    private async Task ReloadSavedProductAsync(int id)
    {
        var saved = await _productRepository.GetByIdAsync(id, CancellationToken.None);
        if (saved is not null)
        {
            _productCache.AddOrUpdate(saved);
            _detailProductId = id;
            SelectedProduct = saved;

            // Until the reload arrives the form still shows its new sections as new, though Save has just kept each on
            // the Sub-Assemblies screen: saved again from here, each would be kept a second time. So Save waits, and the
            // tabs ignore the mouse, as they do while another product's details are on their way; the reload readies them.
            AreDetailsReady = false;
        }
    }

    /// <summary>The add form, fresh for its mode: a new sub-assembly's name, role and lines, or the pick of another product's.</summary>
    private void StartUsageFormMode()
    {
        var mode = EditingUsageMode;
        IsFormEditingLines = mode != UsageFormMode.Link;
        IsFormPickingSource = mode != UsageFormMode.New;

        // The title is the one thing on the form that says which way in was picked, now that the picker that used to
        // say it is on Add Sub-Assembly's drop-down instead. Set here, not in StartAddingUsage, so it follows another
        // item picked while the form is open.
        UsageFormTitle = mode switch
        {
            UsageFormMode.Link => "Link an existing sub-assembly",
            UsageFormMode.Copy => "Copy an existing sub-assembly",
            _ => "New sub-assembly",
        };
        FormLines.Clear();
        FormAddIns.Clear();
        FormPackagingLines.Clear();
        IsFormAddInsExpanded = false;
        SelectedFormLine = null;
        SelectedFormAddIn = null;
        SelectedFormPackagingLine = null;
        RefreshLinkableOptions();

        // The first role the recipe doesn't have yet, so sections can be added in role order without picking.
        var takenRoleIds = UsageEdits.Select(u => u.RoleId).ToHashSet();
        EditingUsageRole = FormRoleOptions.FirstOrDefault(r => !takenRoleIds.Contains(r.Id)) ?? FormRoleOptions.FirstOrDefault();
        EditingUsageName = null;
        EditingUsageSource = null;
        SetPieceSize(UsageAuthoredField.WeightPerPiece, 0m, 0m);
    }

    /// <summary>
    /// After a sub-assembly was picked to link or copy: its lines, for the batch weight, and for a copy
    /// also its role, all editable from then on. Packaging picked instead fills the packaging table: a set's lines, or
    /// the product's packaging as it shows (through any link of its own), and a copy takes the Packaging role.
    /// </summary>
    private async Task LoadUsageSourceAsync(LinkableSubAssemblyOption? source, CancellationToken cancellationToken)
    {
        IReadOnlyList<SubAssemblyLineEdit> lines = [];
        IReadOnlyList<PackagingLineRow> packagingRows = [];
        if (source?.PackagingTarget is { } packaging)
        {
            packagingRows = packaging.SubAssemblyId is { } setId
                ? [.. (await _subAssemblyRepository.GetPackagingLinesAsync(setId, cancellationToken))
                    .Select(l => new PackagingLineRow(FindPackagingMaterial(l.PackagingMaterialId) ?? l.PackagingMaterial!, l.QtyPerSet, l.UnitId))]
                : [.. (await _packagingLineRepository.GetEffectiveForProductAsync(packaging.ProductId!.Value, cancellationToken)).Select(ToPackagingRow)];
        }
        else if (source?.SubAssembly is { } subAssembly)
        {
            var saved = await _subAssemblyLineRepository.GetForSubAssembliesAsync([subAssembly.Id], cancellationToken);
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
        SelectedFormPackagingLine = null;
        FormPackagingLines.Clear();
        FormPackagingLines.AddRange(packagingRows);

        // A copy takes its source's role; packaging, the Packaging role. Not its name, which is another product's and
        // role's ("Cheese Roll Filling: Cheese"): Save names the copy after this product.
        if (EditingUsageMode == UsageFormMode.Copy && source is not null)
        {
            EditingUsageRole = source.IsPackaging
                ? FormRoleOptions.FirstOrDefault(r => r.IsPackaging) ?? EditingUsageRole
                : FormRoleOptions.FirstOrDefault(r => r.Id == source.SubAssembly!.RoleId) ?? EditingUsageRole;
            EditingUsageName = null;
        }

        FormLinesChanged();
    }

    /// <summary>
    /// Applies the form and returns the tab that shows what it applied: the sub-assembly's own, or the Packaging tab
    /// for packaging (IsFormPackaging).
    /// </summary>
    private RecipeTab ApplyForm() => IsFormPackaging ? ApplyPackagingForm() : TabFor(ApplyUsageForm());

    /// <summary>
    /// The add form's packaging, put in as the product's: a new or copied one's lines as its own (to type into on the
    /// Packaging tab), or a link, whose lines only show. Save writes it. A product has one packaging, so the form only
    /// offers it while there's none (RefreshFormRoleOptions, RefreshLinkableOptions).
    /// </summary>
    private PackagingRecipeTab ApplyPackagingForm()
    {
        var linking = EditingUsageMode == UsageFormMode.Link;
        var target = EditingUsageSource?.PackagingTarget;
        if (linking && target is null)
        {
            throw new InvalidOperationException("Pick the packaging to link, or cancel the form.");
        }

        var lines = PackagingLineRow.CompleteLines(FormPackagingLines);
        if (lines.Count == 0)
        {
            throw new InvalidOperationException(linking
                ? $"{target!.Name} is still loading its packaging, or has none. Try again in a moment, or pick another."
                : "Add the packaging materials one piece takes.");
        }

        ShowPackagingLines(lines.Select(l => new PackagingLineRow(l.PackagingMaterial, l.QtyPerUnit)));
        ShowPackagingLink(linking ? target : null);
        SetHasPackaging(true);
        CloseUsageForm();
        RefreshLinkableOptions();
        RecomputeCosts();
        return PackagingTab;
    }

    /// <summary>
    /// Puts the form's sub-assembly in the recipe and closes the form, without moving the tab showing: the caller
    /// decides which tab follows, since Apply shows the sub-assembly's own and a tab change shows the one left for.
    /// An edit that changes nothing is left alone, so merely looking at a section doesn't churn the recipe.
    /// </summary>
    private ProductUsageEdit ApplyUsageForm()
    {
        var authoredValue = CurrentAuthoredValue();
        var shrinkagePercent = CurrentShrinkagePercent();
        ProductUsageEdit usage;

        if (EditingUsage is { IsLinked: true } linked)
        {
            usage = linked.WithPieceSizeAndAddIns(
                _usageAuthoredField, authoredValue, shrinkagePercent, CompleteAddIns(linked.DisplayName, linked.Lines));
        }
        else if (IsFormEditingLines)
        {
            var role = EditingUsageRole
                ?? throw new InvalidOperationException("Pick the sub-assembly's role. It's what the sub-assembly is called.");

            var lines = IngredientLineRow.CompleteLines(FormLines, role.Name);
            if (lines.Count == 0)
            {
                throw new InvalidOperationException($"Add the ingredients that go into the {role.Name}, in grams.");
            }

            // One of the product's own keeps its id, so Save rewrites it in place; a new one has none yet.
            usage = new ProductUsageEdit(
                EditingUsage is { IsOwned: true } own ? own.SubAssemblyId : 0, role, [.. lines.Select(l => new SubAssemblyLineEdit(l.Ingredient, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))], _usageAuthoredField, authoredValue, shrinkagePercent, linkedName: null, name: EditingUsageName);
        }
        else
        {
            var source = EditingUsageSource?.SubAssembly
                ?? throw new InvalidOperationException("Pick the sub-assembly to link, or cancel the form.");
            var sourceName = source.Name;

            // The table's read-only lines are the ones LoadUsageSourceAsync loaded for this very pick; a pick
            // whose lines are still on their way has none yet, and linking it would make a batch of nothing.
            if (FormLines.Count == 0)
            {
                throw new InvalidOperationException($"{sourceName} is still loading its ingredients. Try again in a moment.");
            }

            IReadOnlyList<SubAssemblyLineEdit> sourceLines =
                [.. FormLines.Where(l => l.Ingredient is not null).Select(l => new SubAssemblyLineEdit(l.Ingredient!, l.Grams ?? 0m, l.IsFormulaBase, l.AuthoredBakersPercent))];

            var role = Roles.FirstOrDefault(r => r.Id == source.RoleId) ?? source.Role;
            usage = new ProductUsageEdit(
                source.Id,
                role,
                sourceLines,
                _usageAuthoredField,
                authoredValue,
                shrinkagePercent,
                source.Name,
                CompleteAddIns(sourceName, sourceLines),
                SectionLabelOf(source.Name, role));
        }

        if (EditingUsage is { } unchanged && usage.ShowsSameAs(unchanged))
        {
            CloseUsageForm();
            return unchanged;
        }

        // Sections in one role told apart by their names. A linked one being edited keeps its role and name, which are
        // the Sub-Assemblies screen's to change; and a section left as it was returned above, so it can still be looked
        // through, and Save says what's wrong when it's next changed.
        if (EditingUsage is not { IsLinked: true })
        {
            EnsureSectionsDistinct(usage, EditingUsage);
        }

        if (EditingUsage is { } editing)
        {
            ReplaceInRecipeOrder(editing, usage);
        }
        else
        {
            InsertInRecipeOrder(usage);
        }

        CloseUsageForm();
        RefreshLinkableOptions();
        RecomputeCosts();
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

        // An add form nothing was entered in has nothing to apply: it just closes, and its tab goes.
        if (IsUsageFormUntouched())
        {
            CloseUsageForm();
            return true;
        }

        // Applying a section is one undo step, wherever it's applied from: a tab left, or a Save. A refused one
        // changes only the message and whether the add-ins table is open, neither of which counts as a change, so
        // the step settles into nothing of its own accord. An apply closes the form, which isn't a change either:
        // the step is measured by what it did to the recipe, as Save's is (WithFormOf), so a tab left with nothing
        // typed into its form records nothing.
        History.Begin();
        try
        {
            ErrorMessage = null;
            ApplyForm();
            return true;
        }
        catch (InvalidOperationException ex)
        {
            ErrorMessage = ex.Message;
            return false;
        }
        finally
        {
            History.End((before, now) => before.WithFormOf(now));
        }
    }

    /// <summary>
    /// The add-ins table as lines, refused if one is an ingredient <paramref name="baseLines"/> already has.
    /// The repository checks the same rule as it writes them; this is what puts the message on the screen
    /// the moment Apply is pressed, or the tab is left. Refused, the table opens, if Hide Add-Ins had shut it,
    /// so the line the message is about can be seen.
    /// </summary>
    private IReadOnlyList<SubAssemblyLineEdit> CompleteAddIns(string subAssemblyName, IReadOnlyList<SubAssemblyLineEdit> baseLines)
    {
        try
        {
            var addIns = IngredientLineRow.CompleteLines(FormAddIns, subAssemblyName);
            ProductRules.EnsureValidAddIns(
                [.. addIns.Select(a => (a.Ingredient.Id, a.Grams, a.Ingredient.Name))],
                subAssemblyName,
                [.. baseLines.Select(l => l.Ingredient.Id)]);

            return [.. addIns.Select(a => new SubAssemblyLineEdit(a.Ingredient, a.Grams))];
        }
        catch (InvalidOperationException)
        {
            IsFormAddInsExpanded = true;
            throw;
        }
    }

    /// <summary>
    /// Refuses <paramref name="usage"/> in place of <paramref name="replacing"/> when the recipe could no longer tell its
    /// sections apart: two in one role by the same name, or both without one (SubAssemblyRules.EnsureSectionsDistinct),
    /// or a name too long. SetRecipeAsync checks the same as it saves; this is what puts the message up the moment
    /// Apply is pressed, or the tab is left.
    /// </summary>
    private void EnsureSectionsDistinct(ProductUsageEdit usage, ProductUsageEdit? replacing)
    {
        List<ProductUsageEdit> sections = [.. UsageEdits.Where(u => !ReferenceEquals(u, replacing)), usage];
        SubAssemblyRules.EnsureSectionsDistinct([.. sections.Select(u => (u.RoleId, u.RoleName, u.Label))]);
        SubAssemblyRules.EnsureValidSectionName(usage.IsEditable ? usage.Name : null);
    }

    /// <summary>
    /// Tells each section whether the recipe has others in its role, so it's called by its name too ("Filling: Ube"),
    /// and its tab reads so. Run whenever the recipe's sections change.
    /// </summary>
    private void MarkRepeatedRoles()
    {
        var countsByRole = UsageEdits.CountBy(u => u.RoleId).ToDictionary();
        foreach (var usage in UsageEdits)
        {
            usage.IsRoleRepeated = countsByRole[usage.RoleId] > 1;
        }

        foreach (var tab in RecipeTabs.OfType<SubAssemblyRecipeTab>())
        {
            tab.RefreshHeader();
        }
    }

    private void CloseUsageForm()
    {
        IsUsageFormOpen = false;
        EditingUsage = null;
        RebaseSettledForm();
    }

    /// <summary>
    /// The sub-assembly form now holds another section's values, or none, without anything having been edited,
    /// so what Save measures itself against follows it: clicking through the recipe's tabs isn't a change to
    /// write. Only the form and the tabs move (ProductFormSnapshot.WithFormOf) - the recipe a tab left has
    /// already been applied into the sections by then, and those are measured as they were saved.
    ///
    /// Not while a restore is putting a form back: there the form's values are the user's, stepped back to.
    /// </summary>
    private void RebaseSettledForm()
    {
        if (!IsBusyApplying)
        {
            History.Rebase((settled, now) => settled.WithFormOf(now));
        }
    }

    /// <summary>The form on <paramref name="usage"/>, as it was last applied: the whole of its tab.</summary>
    private void OpenUsageForm(ProductUsageEdit usage)
    {
        EditingUsage = usage;
        IsAddingUsage = false;
        UsageFormTitle = usage.IsEditable ? $"Edit {usage.SectionName}" : $"Edit {usage.SectionName} ({usage.LinkedFromText})";
        IsFormEditingLines = usage.IsEditable;
        IsFormPickingSource = false;
        EditingUsageSource = null;
        RefreshFormRoleOptions();
        EditingUsageRole = FormRoleOptions.FirstOrDefault(r => r.Id == usage.RoleId) ?? usage.Role;
        EditingUsageName = usage.Name;

        // A linked one's lines show too, read-only: they're the Sub-Assemblies screen's. What is this product's is its piece
        // size and its add-ins, which go in the table under them, open if there are any.
        ShowFormLines(usage.Lines);
        ShowFormAddIns(usage.AddIns);
        IsFormAddInsExpanded = usage.AddIns.Count > 0;
        SelectedFormLine = null;
        SelectedFormAddIn = null;
        SetPieceSize(usage.AuthoredField, usage.AuthoredValue, usage.ShrinkagePercent);
        IsUsageFormOpen = true;
        RebaseSettledForm();
    }

    /// <summary>
    /// A fresh form to add a sub-assembly, in place of any the form had open. It starts in New, and AddUsageCommand
    /// sets the way its drop-down item asked for straight after, which starts the form again in it.
    /// </summary>
    private void StartAddingUsage()
    {
        EditingUsage = null;
        IsAddingUsage = true;
        EditingUsageMode = UsageFormMode.New;
        IsUsageFormOpen = true;
        StartUsageFormMode();
        RebaseSettledForm();
    }

    /// <summary>The add form as it opened: nothing picked to link or copy, no line and no piece size. Its role alone isn't an entry.</summary>
    private bool IsUsageFormUntouched() =>
        IsAddingUsage
        && EditingUsageSource is null
        && string.IsNullOrWhiteSpace(EditingUsageName)
        && FormLines.All(l => l.IngredientId is null && l.Grams == 0)
        && FormAddIns.All(l => l.IngredientId is null && l.Grams == 0)
        && FormPackagingLines.All(l => l.PackagingMaterialId is null && l.QtyPerUnit == 0)
        && CurrentAuthoredValue() == 0
        && EditingUsageShrinkagePercent == 0;

    /// <summary>The form's lines changed, or its source's were loaded: the batch weight, the other piece-size figure and the preview follow.</summary>
    private void FormLinesChanged()
    {
        this.RaisePropertyChanged(nameof(IsFormShowingLines));
        this.RaisePropertyChanged(nameof(IsFormOfferingAddIns));
        this.RaisePropertyChanged(nameof(IsFormShowingAddIns));
        this.RaisePropertyChanged(nameof(FormAddInsToggleText));
        ResyncPieceSize();
    }

    /// <summary>Shows a piece size and shrinkage in the form, with the other figure worked out, without any of it counting as typed.</summary>
    private void SetPieceSize(UsageAuthoredField authoredField, decimal authoredValue, decimal shrinkagePercent)
    {
        _usageAuthoredField = authoredField;
        _syncingPieceSize = true;
        try
        {
            EditingUsageShrinkagePercent = shrinkagePercent;
            if (authoredField == UsageAuthoredField.WeightPerPiece)
            {
                EditingUsageWeightPerPiece = authoredValue;
            }
            else
            {
                EditingUsageYield = authoredValue;
            }
        }
        finally
        {
            _syncingPieceSize = false;
        }

        ResyncPieceSize();
    }

    private void PieceSizeTyped(UsageAuthoredField authoredField)
    {
        _usageAuthoredField = authoredField;
        ResyncPieceSize();
    }

    /// <summary>
    /// Whatever the piece size is worked out from has changed — a line, an add-in, the shrinkage, or the box
    /// the user just typed in: the other figure follows, and the form's figures with it.
    /// </summary>
    private void ResyncPieceSize()
    {
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
        var finalWeight = ProductRules.CalculateFinalWeight(UsageFormBatch().Sum(l => l.Grams), CurrentShrinkagePercent());
        var (weightPerPiece, yield) = ProductRules.ResolvePieceSize(_usageAuthoredField, CurrentAuthoredValue(), finalWeight);
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
    /// The form's shrinkage, clamped to what a batch can actually lose (EnsureValidUsages). The box stops at
    /// 99.99, so this only catches a figure that reached the form another way; without it a batch could work
    /// out to nothing or less and the preview would price a piece at a negative.
    /// </summary>
    private decimal CurrentShrinkagePercent() => Math.Clamp(EditingUsageShrinkagePercent, 0m, 99.99m);

    /// <summary>
    /// The form's batch, as each line's grams and buffered cost per gram: both its tables, the recipe and
    /// this product's add-ins on top of it, leaving out any row with no ingredient picked yet. Everything
    /// the form works out from the batch — the total weight, the other piece-size figure, the caption and
    /// the cost preview — reads it here, so add-ins reach all of them at once.
    /// </summary>
    private IReadOnlyList<(decimal Grams, decimal CostPerGram)> UsageFormBatch() =>
        [.. FormLines.Concat(FormAddIns).Where(l => l.Ingredient is not null).Select(l => (l.Grams ?? 0m, l.Ingredient!.BufferedCostPerBaseUnit))];

    /// <summary>The form's batch weight, which figure stays put, and what a piece costs.</summary>
    private void RecomputeUsageForm()
    {
        // Packaging is one set of lines per piece, with no batch to share out.
        if (IsFormPackaging)
        {
            var packagingCost = FormPackagingLines.Sum(l => l.CostPerUnit);
            UsageFormPreviewText = FormPackagingLines.Any(l => l.PackagingMaterial is not null) ? $"{packagingCost:N4} a piece" : string.Empty;
            return;
        }

        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(UsageFormBatch());
        var shrinkagePercent = CurrentShrinkagePercent();
        var finalWeight = ProductRules.CalculateFinalWeight(totalWeight, shrinkagePercent);
        var authoredValue = CurrentAuthoredValue();
        var (weightPerPiece, _) = ProductRules.ResolvePieceSize(_usageAuthoredField, authoredValue, finalWeight);

        UsageFormTotalWeightText = $"{totalWeight:#,0.##} g";
        UsageFormFinalWeightText = $"{finalWeight:#,0.##} g";
        // Neither figure is needed to put the section in (ProductRules.EnsureValidUsages), so with none typed the
        // caption says what that costs: nothing per piece, until one is. With a shrinkage it says which batch the
        // pair is read against, since that's the figure that stops matching the lines above.
        UsageFormPieceSizeCaption = authoredValue <= 0
            ? "Type the weight per piece or the yield; the other is worked out. Until then this section costs nothing per piece."
            : shrinkagePercent > 0
                ? _usageAuthoredField == UsageAuthoredField.WeightPerPiece
                    ? $"A finished piece weighs {authoredValue:#,0.##} g, out of the {finalWeight:#,0.##} g the batch makes; it stays there if the batch changes."
                    : $"The yield stays at {authoredValue:#,0.##} pieces, out of the {finalWeight:#,0.##} g the batch makes, if the batch changes."
                : _usageAuthoredField == UsageAuthoredField.WeightPerPiece
                    ? $"Weight per piece stays at {authoredValue:#,0.##} g if the batch changes."
                    : $"The yield stays at {authoredValue:#,0.##} pieces if the batch changes.";

        var costPerPiece = finalWeight > 0 && weightPerPiece > 0
            ? ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece, finalWeight)
            : (decimal?)null;
        UsageFormPreviewText = costPerPiece is { } preview ? $"{preview:N4} a piece" : string.Empty;
        UsageFormCostPerPieceText = costPerPiece is { } figure ? $"{figure:N4}" : "—";

        // The formula's base and the batch, given to both tables: an add-in is read against the recipe's base, never its own,
        // and with no base every line of both is read against the whole batch, add-ins included, so they come to 100%.
        var formulaBase = IngredientLineRow.ApplyFormulaBase([.. FormLines], totalWeight);
        _formFormulaBase = formulaBase;
        IngredientLineRow.ApplyFormulaBase(FormAddIns, formulaBase, totalWeight);
        UsageFormPercentHeader = IngredientLineRow.PercentHeader(formulaBase);
        UsageFormBaseCaption = formulaBase > 0
            ? $"Base {formulaBase:#,0.##} g · whole formula {totalWeight / formulaBase * 100m:#,0.#}%"
            : SubAssembliesViewModel.NoBaseCaption;

        // The arithmetic of the two tables, in words: the recipe's grams, this product's on top, the batch.
        var recipeName = EditingUsage?.DisplayName
            ?? EditingUsageSource?.SubAssembly?.Name ?? "the recipe";
        var addInWeight = FormAddIns.Where(l => l.Ingredient is not null).Sum(l => l.Grams);
        var baseWeight = totalWeight - addInWeight;
        UsageFormAddInsCaption = addInWeight > 0
            ? $"{baseWeight:#,0.##} g from {recipeName} + {addInWeight:#,0.##} g of add-ins = a {totalWeight:#,0.##} g batch."
            : $"Nothing extra yet — the batch is {recipeName}'s {baseWeight:#,0.##} g.";
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
        RecomputeCosts();

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
                var previous = (ProductUsageEdit)e.OldItems![0]!;
                ((SubAssemblyRecipeTab)RecipeTabs[e.NewStartingIndex]).Usage = replacement;
                if (ReferenceEquals(previous, EditingUsage))
                {
                    if (replacement.IsEditable != previous.IsEditable)
                    {
                        // Its role was made product-owned in Settings, or no longer is: the form opens on it again, its
                        // lines now the product's to type into, or the Sub-Assemblies screen's to show.
                        OpenUsageForm(replacement);
                    }
                    else
                    {
                        EditingUsage = replacement;

                        // A linked one's lines aren't the form's to keep: show the Sub-Assemblies screen's, as they now are.
                        if (replacement.IsLinked)
                        {
                            ShowFormLines(replacement.Lines);
                        }
                    }
                }

                break;
            }

            case NotifyCollectionChangedAction.Move:
                RecipeTabs.Move(e.OldStartingIndex, e.NewStartingIndex);
                break;

            default:
                while (RecipeTabs.Count > 0 && RecipeTabs[0] is SubAssemblyRecipeTab)
                {
                    RecipeTabs.RemoveAt(0);
                }

                break;
        }
    }

    private SubAssemblyRecipeTab TabFor(ProductUsageEdit usage) =>
        RecipeTabs.OfType<SubAssemblyRecipeTab>().First(t => ReferenceEquals(t.Usage, usage));

    /// <summary>
    /// Shows <paramref name="tab"/>, and does what showing it does even when it was already showing. The second
    /// RecipeTabShown comes once the tab strip has moved to the tab too, so it's the one that can take New
    /// Sub-Assembly's tab away.
    /// </summary>
    private void SelectRecipeTab(RecipeTab? tab)
    {
        _selectingRecipeTab = true;
        try
        {
            SelectedRecipeTab = tab;
        }
        finally
        {
            _selectingRecipeTab = false;
        }

        RecipeTabShown(tab);
    }

    /// <summary>
    /// A tab was picked in the strip, or SelectedRecipeTab set. A pick the screen makes itself (SelectRecipeTab,
    /// or the tabs rearranged) belongs to whatever step it's part of. The user's own runs between steps: WPF selects
    /// a tab as it takes the cursor, before the field it takes it from hears so (TabItem.OnPreviewGotKeyboardFocus),
    /// so that field's step is still open while the form changes over to the other tab's section, and closed after,
    /// it would record the change of section as an edit. So the step closes on the tab being left, keeping what
    /// was typed there, and another opens on the tab arrived at, for the field to close.
    /// </summary>
    private void RecipeTabPicked(RecipeTab? tab)
    {
        if (_selectingRecipeTab || _arrangingRecipeTabs || _revertingRecipeTab || IsBusyApplying || !History.IsStepOpen)
        {
            RecipeTabShown(tab);
            return;
        }

        History.End();
        try
        {
            RecipeTabShown(tab);
        }
        finally
        {
            History.Begin();
        }
    }

    /// <summary>
    /// A tab was picked, by the user or the screen. Leaving one applies the form it was showing, so nothing typed
    /// is lost; a rule refusing it keeps the tab it was on, with the message shown. A sub-assembly's tab then
    /// opens its form, which is all it shows, and New Sub-Assembly opens a form to add one (a form already adding
    /// one is left as it is). So a form still open is always one Save should apply. New Sub-Assembly's tab only
    /// lasts while it shows: once another does, its add has been applied or had nothing in it, and it goes.
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

        // Not while SelectRecipeTab's pick is still reaching the tab strip, which would still be showing it.
        if (tab is not AddSubAssemblyRecipeTab && !_selectingRecipeTab)
        {
            RemoveAddSubAssemblyTab();
        }
    }

    /// <summary>Takes New Sub-Assembly's tab out of the strip, if it's there, with the strip's own pick ignored.</summary>
    private void RemoveAddSubAssemblyTab()
    {
        if (!RecipeTabs.Contains(AddSubAssemblyTab))
        {
            return;
        }

        _arrangingRecipeTabs = true;
        try
        {
            RecipeTabs.Remove(AddSubAssemblyTab);
        }
        finally
        {
            _arrangingRecipeTabs = false;
        }
    }

    /// <summary>
    /// After the sections were rebuilt (a product loaded, reloaded after Save, or cleared for a new one): the
    /// tab that showed if it's still there (the same sub-assembly's, or Packaging), else the first
    /// sub-assembly's, which is the first in role order, else Packaging. Another product's recipe has no tab
    /// that showed (<paramref name="previous"/> is null), so it opens on its first.
    /// </summary>
    /// <param name="previousIds">The sub-assemblies the recipe used before, which a new section saved since isn't among.</param>
    private void ReselectRecipeTab(RecipeTab? previous, ProductUsageEdit? previousUsage, HashSet<int> previousIds)
    {
        RecipeTab? tab = previous is PackagingRecipeTab ? PackagingTabIfShown : null;
        if (previousUsage is not null)
        {
            var tabs = RecipeTabs.OfType<SubAssemblyRecipeTab>();

            // A linked sub-assembly is known by its id. A new section saved since is linked now, under an id the recipe
            // didn't have, in its role, and by the name it went by when it can be.
            tab = previousUsage.SubAssemblyId > 0
                ? tabs.FirstOrDefault(t => t.Usage.SubAssemblyId == previousUsage.SubAssemblyId)
                : tabs.Where(t => t.Usage.RoleId == previousUsage.RoleId && !previousIds.Contains(t.Usage.SubAssemblyId))
                    .OrderBy(t => t.Usage.Name == previousUsage.Name ? 0 : 1)
                    .FirstOrDefault();
        }

        // A product with neither has no tab to show: the card says how to start.
        SelectRecipeTab(tab ?? RecipeTabs.OfType<SubAssemblyRecipeTab>().FirstOrDefault() ?? PackagingTabIfShown);
    }

    /// <summary>
    /// The sub-assemblies the form can link or copy: every one, less (when linking) the ones the recipe already links.
    /// Every one is offered to copy. A pick that's no longer offered is cleared. The roles the form offers follow the
    /// recipe too (RefreshFormRoleOptions).
    /// </summary>
    private void RefreshLinkableOptions()
    {
        var linking = EditingUsageMode == UsageFormMode.Link;
        var usedIds = UsageEdits.Select(u => u.SubAssemblyId).ToHashSet();

        // Packaging comes after the recipes, while the product has none: the packaging sets, then every other product's
        // packaging.
        LinkableSubAssemblies.MergeFrom(
            [
                .. _linkable
                    .Where(s => linking
                        ? s.OwnerProductId is null && !usedIds.Contains(s.Id)
                        : s.OwnerProductId is null || s.OwnerProductId != _detailProductId)
                    .Select(s => new LinkableSubAssemblyOption(s)),
                .. HasPackaging ? [] : _packagingSets.Select(s => new LinkableSubAssemblyOption(s)),
                .. HasPackaging ? [] : Products.Where(p => p.Id != _detailProductId).Select(p => new LinkableSubAssemblyOption(p)),
            ],
            o => o.Key);

        if (EditingUsageSource is { } picked && !LinkableSubAssemblies.Contains(picked))
        {
            EditingUsageSource = null;
        }

        RefreshFormRoleOptions();
    }

    /// <summary>
    /// The roles the form offers (FormRoleOptions): every recipe role, as a recipe can have several sub-assemblies in one,
    /// told apart by their names. The role picked stays picked while it's still offered: the picker can drop its pick as
    /// its list changes under it.
    /// </summary>
    private void RefreshFormRoleOptions()
    {
        var pickedRoleId = EditingUsageRole?.Id;
        // The Packaging role adds the product's packaging, which isn't a recipe section: offered only to add, and only
        // while there's none. A section already in the recipe can't turn into packaging.
        FormRoleOptions.MergeFrom(
            [.. Roles.Where(r => !r.IsPackaging || (IsAddingUsage && !HasPackaging))],
            r => r.Id);
        EditingUsageRole = FormRoleOptions.FirstOrDefault(r => r.Id == pickedRoleId);
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
            var saved = edit.SubAssemblyId > 0 ? linkedById.GetValueOrDefault(edit.SubAssemblyId) : null;
            var isOwnNow = saved is not null && saved.OwnerProductId is { } ownerId && ownerId == _detailProductId;
            if (saved is not null && edit.IsLinked && isOwnNow)
            {
                // Its role was made product-owned: the product owns it now, with any add-ins it had merged into its lines.
                var role = Roles.FirstOrDefault(r => r.Id == saved.RoleId) ?? saved.Role;
                fresh = new ProductUsageEdit(
                    saved.Id,
                    role,
                    [.. linkedLinesById[saved.Id].Select(l => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    edit.AuthoredField,
                    edit.AuthoredValue,
                    edit.ShrinkagePercent,
                    linkedName: null,
                    name: SectionLabelOf(saved.Name, role));
            }
            else if (edit.IsLinked || (edit.IsOwned && saved is not null && !isOwnNow))
            {
                // A linked one, or one of its own whose role is no longer product-owned, which it links now.
                if (saved is not { } subAssembly)
                {
                    continue;
                }

                // Its lines, name and role come fresh from the Sub-Assemblies screen; its add-ins are this product's own,
                // so they only need their ingredients' new costs.
                var role = Roles.FirstOrDefault(r => r.Id == subAssembly.RoleId) ?? subAssembly.Role;
                fresh = edit.WithLinked(
                    subAssembly.Name,
                    SectionLabelOf(subAssembly.Name, role),
                    role,
                    [.. linkedLinesById[subAssembly.Id].Select(l => new SubAssemblyLineEdit(ingredientsById.GetValueOrDefault(l.IngredientId) ?? l.Ingredient!, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent))],
                    Repriced(edit.AddIns, ingredientsById));
            }
            else
            {
                fresh = edit.With(Roles.FirstOrDefault(r => r.Id == edit.RoleId) ?? edit.Role, Repriced(edit.Lines, ingredientsById));
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

        // The packaging lines at their materials' refreshed costs and units, in place, so a line being typed isn't lost.
        PackagingLineRow.Resolve(LineEdits, PackagingMaterials.ToDictionary(m => m.Id));
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
    private static IReadOnlyList<SubAssemblyLineEdit> Repriced(IReadOnlyList<SubAssemblyLineEdit> lines, Dictionary<int, RawMaterial> ingredientsById) =>
        [.. lines.Select(l => ingredientsById.TryGetValue(l.Ingredient.Id, out var fresh) && !CollectionMerge.HasSameValues(fresh, l.Ingredient)
            ? new SubAssemblyLineEdit(fresh, l.Grams, l.IsFormulaBase, l.AuthoredBakersPercent)
            : l)];

    /// <summary>The cost summary, from the recipe and packaging as they are now and the costing fields.</summary>
    private void RecomputeCosts()
    {
        Costs = new ProductCostSummary(
            [.. UsageEdits.Select(u => u.CostPerPiece)],
            EditingQuantityRequired,
            [.. LineEdits.Select(l => l.CostPerUnit)],
            EditingLaborPercent,
            EditingOverheadPercent,
            EditingAdministrativePercent,
            EditingMarginPercent,
            EditingVatPercent,
            EditingCurrentPrice);
    }

    public void Dispose()
    {
        _productCache.Dispose();
        _categoryCache.Dispose();
        _addNewCategoryOption.Dispose();
        _subAssemblyOpenRequests.Dispose();
    }
}
