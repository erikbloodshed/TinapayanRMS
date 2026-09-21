namespace TinapayanRMS.ApplicationCore.Rules;

public static class CostingRules
{
    public static void EnsureValidParameters(decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal marginPercent, decimal vatPercent)
    {
        if (laborPercent < 0 || overheadPercent < 0 || administrativePercent < 0)
        {
            throw new InvalidOperationException("Labor, overhead and administrative percentages can't be negative.");
        }

        if (marginPercent < 0 || vatPercent < 0)
        {
            throw new InvalidOperationException("Margin and VAT percentages can't be negative.");
        }
    }

    /// <summary>
    /// A cost entered as a percentage of the sub-assembly (ingredient) cost — labor, overhead,
    /// administrative — in pesos per piece: 25% labor on ₱20 of sub-assemblies is ₱5. Packaging is
    /// deliberately left out of the base: what these three pay for is making the recipe, not wrapping it.
    /// </summary>
    public static decimal CalculateCostFromSubAssemblyPercent(decimal subAssemblyCostPerPiece, decimal percent) =>
        subAssemblyCostPerPiece * percent / 100m;

    /// <summary>
    /// Cost-plus pricing: margin is a markup on cost (BaseCost × (1 + Margin%)), and VAT is
    /// added on top of that pre-VAT price (× (1 + VAT%)) as a separate step — never combined
    /// into one formula, so each input maps to exactly one step a shop owner can check by hand.
    /// The base cost is the materials — the sub-assemblies plus the packaging — plus the labor, overhead
    /// and administrative costs, each of those a percentage of the sub-assemblies alone
    /// (CalculateCostFromSubAssemblyPercent).
    /// </summary>
    public static (decimal BaseCost, decimal PreVatPrice, decimal SellingPrice) CalculateSellingPrice(
        decimal subAssemblyCostPerPiece,
        decimal packagingCostPerPiece,
        decimal laborPercent,
        decimal overheadPercent,
        decimal administrativePercent,
        decimal marginPercent,
        decimal vatPercent)
    {
        var laborCost = CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece, laborPercent);
        var overheadCost = CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece, overheadPercent);
        var administrativeCost = CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece, administrativePercent);
        var baseCost = subAssemblyCostPerPiece + packagingCostPerPiece + laborCost + overheadCost + administrativeCost;
        var preVatPrice = baseCost * (1 + marginPercent / 100m);
        var sellingPrice = preVatPrice * (1 + vatPercent / 100m);
        return (baseCost, preVatPrice, sellingPrice);
    }

    /// <summary>
    /// The profit on one piece as a percentage of the price before VAT: the markup's share of what
    /// the shop keeps, since VAT is collected for the government. A 30% markup is a 23.08% margin.
    /// Informational only; the selling price still comes from the markup (CalculateSellingPrice).
    /// </summary>
    public static decimal CalculateProfitMarginPercent(decimal baseCost, decimal preVatPrice) =>
        PercentOf(preVatPrice - baseCost, preVatPrice);

    /// <summary>
    /// The markup a price with VAT actually earns, as a percentage of the base cost: VAT taken out first, as
    /// CalculateSellingPrice adds it last. The price CalculateSellingPrice works out gives back its margin.
    /// </summary>
    public static decimal CalculateMarkupPercentFromPrice(decimal baseCost, decimal sellingPrice, decimal vatPercent)
    {
        var preVatPrice = sellingPrice / (1 + vatPercent / 100m);
        return PercentOf(preVatPrice - baseCost, baseCost);
    }

    /// <summary><paramref name="part"/> as a percentage of <paramref name="whole"/>, or 0 when there's nothing to share.</summary>
    public static decimal PercentOf(decimal part, decimal whole) =>
        whole == 0 ? 0 : part / whole * 100m;
}
