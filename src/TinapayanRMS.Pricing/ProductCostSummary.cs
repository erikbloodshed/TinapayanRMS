using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Pricing;

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
    public static ProductCostSummary Empty { get; } = new(new ProductCostInputs());

    /// <summary>
    /// Works the summary out from <paramref name="inputs"/>. The wastage, labor, overhead and administrative costs are each a
    /// percentage of the recipe cost alone; a composite's products' packaging joins its packaging, so none of them go on it.
    /// </summary>
    public ProductCostSummary(ProductCostInputs inputs)
    {
        Inputs = inputs;
        var quantityRequired = inputs.QuantityRequired;
        IReadOnlyList<decimal> usageCostsPerPiece = [.. inputs.Parts.Select(u => u.CostPerPiece)];
        PriceRoundingStep = inputs.PriceRoundingStep;
        IsComposite = inputs.IsComposite;
        HasSections = inputs.HasOwnSections;
        QuantityRequired = quantityRequired;
        SubAssemblies = usageCostsPerPiece.Sum() * quantityRequired;
        OwnPackaging = inputs.PackagingCosts.Sum();
        ComponentPackaging = inputs.ComponentPackaging;
        Packaging = OwnPackaging + (inputs.ComponentPackaging ?? 0m);
        Material = ProductRules.CalculateMaterialCost(usageCostsPerPiece, quantityRequired, [.. inputs.PackagingCosts, inputs.ComponentPackaging ?? 0m]);
        WastagePercent = inputs.WastagePercent;
        Wastage = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, inputs.WastagePercent);
        LaborPercent = inputs.LaborPercent;
        OverheadPercent = inputs.OverheadPercent;
        AdministrativePercent = inputs.AdministrativePercent;
        Labor = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, inputs.LaborPercent);
        Overhead = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, inputs.OverheadPercent);
        Administrative = CostingRules.CalculateCostFromSubAssemblyPercent(SubAssemblies, inputs.AdministrativePercent);
        MarginPercent = inputs.MarginPercent;
        VatPercent = inputs.VatPercent;
        (BaseCost, PreVatPrice, SellingPrice) = CostingRules.CalculateSellingPrice(
            SubAssemblies,
            Packaging,
            inputs.LaborPercent,
            inputs.OverheadPercent,
            inputs.AdministrativePercent,
            inputs.MarginPercent,
            inputs.VatPercent,
            inputs.WastagePercent);
        CurrentPrice = inputs.CurrentPrice;
        CurrentMarkupPercent = inputs.CurrentPrice is { } price
            ? CostingRules.CalculateMarkupPercentFromPrice(BaseCost, price, inputs.VatPercent)
            : null;
        SubAssemblyLines = [.. inputs.Parts.Select(u =>
        {
            var cost = u.CostPerPiece * quantityRequired;
            return new SubAssemblyCostLine(u.Name, cost, CostingRules.PercentOf(cost, SellingPrice));
        })];
    }

    /// <summary>What the summary was worked out from.</summary>
    public ProductCostInputs Inputs { get; }

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

    /// <summary>What the selling price is rounded up to a multiple of (CostingDefaults.PriceRoundingStep); 0 for none.</summary>
    public decimal PriceRoundingStep { get; }

    /// <summary>The prices are rounded: the rounded selling price shows beside the exact one.</summary>
    public bool HasPriceRounding => PriceRoundingStep > 0m;

    /// <summary>
    /// The selling price rounded up to a price the shop would charge (CostingRules.RoundPriceUp): the exact one
    /// when there's no rounding. Only shown and offered; every figure above it stays exact.
    /// </summary>
    public decimal RoundedSellingPrice => CostingRules.RoundPriceUp(SellingPrice, PriceRoundingStep);

    /// <summary>"Rounded up to the next 0.25: 43.25"; empty without rounding.</summary>
    public string RoundedSellingPriceText => HasPriceRounding
        ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"Rounded up to the next {PriceRoundingStep:0.00}: {RoundedSellingPrice:N2}")
        : string.Empty;

    /// <summary>The current price doesn't cover the base cost: a negative markup, which the header shows in red.</summary>
    public bool IsCurrentPriceBelowCost => CurrentMarkupPercent < 0;

    /// <summary>
    /// The current price earns less than the costing's Markup %, compared as they're shown,
    /// to 2 decimals, so a price that rounds to the target isn't flagged: amber in the header, the Products list and Reports.
    /// </summary>
    public bool IsBelowTargetMarkup => CurrentMarkupPercent is { } earned && Math.Round(earned, 2) < Math.Round(MarginPercent, 2);

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
