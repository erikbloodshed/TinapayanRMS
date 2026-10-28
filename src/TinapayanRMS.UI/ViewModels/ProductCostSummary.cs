using System.Reactive.Linq;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A product's cost for one sellable unit, from its materials to its selling price (cost-plus,
/// CostingRules), with each part's share of the selling price. The recipe is authored per piece, so the
/// usages are multiplied by <c>quantityRequired</c>; the packaging lines already wrap one whole unit.
/// Worked out from the form, so unsaved edits show at once. A composite's "usages" are the products it holds, each
/// line what its quantity of that product brings in recipe cost (CompositeProductRules.CalculateComponentCost), then its
/// own sections, at a quantity of 1: its Sub-assemblies figure is its recipe cost, and every formula after that is the same.
/// </summary>
public sealed class ProductCostSummary
{
    public static ProductCostSummary Empty { get; } = new([], 1, [], 0m, 0m, 0m, 0m, 0m, null);

    /// <param name="wastagePercent">
    /// The shop-wide Wastage % (CostingDefaults.WastagePercent): a share of <see cref="SubAssemblies"/> added on top, for
    /// what's lost making it. A composite's products bring their recipe cost before theirs, so it's added once, here.
    /// </param>

    /// <param name="usages">
    /// Each sub-assembly in the recipe, in recipe order: its name and its cost per piece. For a composite, each product it
    /// holds: "Ensaymada × 6", and what the six bring in recipe cost, then its own sections.
    /// </param>
    /// <param name="packagingCosts">Its own packaging lines, each for one whole sellable unit.</param>
    /// <param name="isComposite">The product is a composite, whose recipe is the products it holds.</param>
    /// <param name="hasSections">A composite also has sections of its own (a topping, a filling), which its labels name too.</param>
    /// <param name="componentPackaging">
    /// What a composite's products bring in packaging, when it includes theirs (Product.IncludesComponentPackaging): it
    /// joins the packaging, so labor, overhead and administrative don't go on it. Null when it doesn't include them.
    /// </param>
    public ProductCostSummary(
        IReadOnlyList<(string Name, decimal CostPerPiece)> usages,
        int quantityRequired,
        IReadOnlyList<decimal> packagingCosts,
        decimal laborPercent,
        decimal overheadPercent,
        decimal administrativePercent,
        decimal marginPercent,
        decimal vatPercent,
        decimal? currentPrice,
        bool isComposite = false,
        bool hasSections = false,
        decimal? componentPackaging = null,
        decimal wastagePercent = 0m)
    {
        IReadOnlyList<decimal> usageCostsPerPiece = [.. usages.Select(u => u.CostPerPiece)];
        IsComposite = isComposite;
        HasSections = hasSections;
        QuantityRequired = quantityRequired;
        SubAssemblies = usageCostsPerPiece.Sum() * quantityRequired;
        OwnPackaging = packagingCosts.Sum();
        ComponentPackaging = componentPackaging;
        Packaging = OwnPackaging + (componentPackaging ?? 0m);
        Material = ProductRules.CalculateMaterialCost(usageCostsPerPiece, quantityRequired, [.. packagingCosts, componentPackaging ?? 0m]);
        WastagePercent = wastagePercent;
        Wastage = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, wastagePercent);
        LaborPercent = laborPercent;
        OverheadPercent = overheadPercent;
        AdministrativePercent = administrativePercent;
        Labor = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, laborPercent);
        Overhead = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, overheadPercent);
        Administrative = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, administrativePercent);
        MarginPercent = marginPercent;
        VatPercent = vatPercent;
        (BaseCost, PreVatPrice, SellingPrice) = CostingRules.CalculateSellingPrice(
            SubAssemblies, Packaging, laborPercent, overheadPercent, administrativePercent, marginPercent, vatPercent, wastagePercent);
        CurrentPrice = currentPrice;
        CurrentMarkupPercent = currentPrice is { } price
            ? CostingRules.CalculateMarkupPercentFromPrice(BaseCost, price, vatPercent)
            : null;
        SubAssemblyLines = [.. usages.Select(u =>
        {
            var cost = u.CostPerPiece * quantityRequired;
            return new SubAssemblyCostLine(u.Name, cost, CostingRules.PercentOf(cost, SellingPrice));
        })];
    }

    /// <summary>How many finished pieces this unit holds: 1 for a product sold by the piece.</summary>
    public int QuantityRequired { get; }

    /// <summary>A composite's costs: its <see cref="SubAssemblies"/> are the recipe cost of the products it holds.</summary>
    public bool IsComposite { get; }

    /// <summary>A composite that also has sections of its own: a topping, a filling.</summary>
    public bool HasSections { get; }

    /// <summary>
    /// What <see cref="SubAssemblies"/> is called in the breakdown: "Sub-assemblies (ingredients)", or a composite's
    /// "Products (ingredients)", "Products and sub-assemblies (ingredients)" once it has sections of its own.
    /// </summary>
    public string PartsTitle => $"{char.ToUpperInvariant(PartsBase[0])}{PartsBase[1..]} (ingredients)";

    /// <summary>What labor, overhead and administrative are a percentage of, in their labels: "sub-assemblies", or a composite's "products" (and "sub-assemblies").</summary>
    public string PartsBase => (IsComposite, HasSections) switch
    {
        (true, true) => "products and sub-assemblies",
        (true, false) => "products",
        _ => "sub-assemblies",
    };

    /// <summary>The recipe cost: every sub-assembly's cost per piece times the pieces a unit holds, or the products a composite holds.</summary>
    public decimal SubAssemblies { get; }

    /// <summary>SubAssemblies broken down: each sub-assembly in the recipe, in recipe order, adding up to it.</summary>
    public IReadOnlyList<SubAssemblyCostLine> SubAssemblyLines { get; }

    /// <summary>All of the packaging for one sellable unit: its own, and a composite's products' when it includes theirs.</summary>
    public decimal Packaging { get; }

    /// <summary>Its own packaging lines alone, what its Packaging tab lists.</summary>
    public decimal OwnPackaging { get; }

    /// <summary>What a composite's products bring in packaging, when it includes theirs; null when it doesn't.</summary>
    public decimal? ComponentPackaging { get; }

    /// <summary>What <see cref="Packaging"/> is called in the breakdown: "Packaging", or "Packaging (own and products')" when a composite includes its products'.</summary>
    public string PackagingTitle => ComponentPackaging is null ? "Packaging" : "Packaging (own and products')";

    public decimal Material { get; }

    /// <summary>The shop-wide Wastage % it's priced with.</summary>
    public decimal WastagePercent { get; }

    /// <summary>The wastage per sellable unit, in pesos: WastagePercent of the sub-assembly cost. It's in the base cost.</summary>
    public decimal Wastage { get; }

    public decimal LaborPercent { get; }

    public decimal OverheadPercent { get; }

    public decimal AdministrativePercent { get; }

    /// <summary>The labor cost per sellable unit, in pesos: LaborPercent of the sub-assembly cost.</summary>
    public decimal Labor { get; }

    /// <summary>The overhead cost per sellable unit, in pesos: OverheadPercent of the sub-assembly cost.</summary>
    public decimal Overhead { get; }

    /// <summary>The administrative cost per sellable unit, in pesos: AdministrativePercent of the sub-assembly cost.</summary>
    public decimal Administrative { get; }

    /// <summary>The subtotal the markup goes on: materials, wastage, labor, overhead and administrative.</summary>
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

    public decimal WastageShare => CostingRules.PercentOf(Wastage, SellingPrice);

    public decimal LaborShare => CostingRules.PercentOf(Labor, SellingPrice);

    public decimal OverheadShare => CostingRules.PercentOf(Overhead, SellingPrice);

    public decimal AdministrativeShare => CostingRules.PercentOf(Administrative, SellingPrice);

    public decimal BaseCostShare => CostingRules.PercentOf(BaseCost, SellingPrice);

    public decimal MarkupShare => CostingRules.PercentOf(Markup, SellingPrice);

    public decimal VatShare => CostingRules.PercentOf(Vat, SellingPrice);

    /// <summary>100% whenever there's a price, for the total row.</summary>
    public decimal SellingPriceShare => CostingRules.PercentOf(SellingPrice, SellingPrice);
}
