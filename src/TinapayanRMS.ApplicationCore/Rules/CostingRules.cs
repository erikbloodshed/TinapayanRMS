namespace TinapayanRMS.ApplicationCore.Rules;

public static class CostingRules
{
    public static void EnsureValidParameters(decimal laborPercent, decimal overheadPercent, decimal administrativePercent, decimal marginPercent, decimal vatPercent)
    {
        if (laborPercent < 0 || overheadPercent < 0 || administrativePercent < 0)
        {
            throw new BusinessRuleException("Labor, overhead and administrative percentages can't be negative.");
        }

        if (marginPercent < 0 || vatPercent < 0)
        {
            throw new BusinessRuleException("Margin and VAT percentages can't be negative.");
        }
    }

    /// <summary>The shop-wide Wastage % (CostingDefaults.WastagePercent): 0 to 100, a share of the ingredient cost added on top of it.</summary>
    public static void EnsureValidWastage(decimal wastagePercent)
    {
        if (wastagePercent is < 0 or > 100)
        {
            throw new BusinessRuleException("The wastage must be from 0% to 100%.");
        }
    }

    /// <summary>The steps a suggested price can be rounded up to (CostingDefaults.PriceRoundingStep): none, then from a quarter to ten.</summary>
    public static readonly IReadOnlyList<decimal> PriceRoundingSteps = [0m, 0.25m, 0.50m, 1m, 5m, 10m];

    public static void EnsureValidPriceRoundingStep(decimal step)
    {
        if (!PriceRoundingSteps.Contains(step))
        {
            throw new BusinessRuleException("Prices can be rounded up to 0.25, 0.50, 1, 5 or 10, or not at all.");
        }
    }

    /// <summary>
    /// <paramref name="price"/> rounded up to the next multiple of <paramref name="step"/> (2026-10-29): 43.24 to 43.25 on
    /// 0.25, to 44 on 1. Up, never down, so a rounded price still earns at least its markup. No step leaves it as it is.
    /// </summary>
    public static decimal RoundPriceUp(decimal price, decimal step) =>
        step <= 0m ? price : Math.Ceiling(price / step) * step;

    /// <summary>
    /// A cost entered as a percentage of the sub-assembly (ingredient) cost — wastage, labor, overhead,
    /// administrative — in pesos per piece: 25% labor on ₱20 of sub-assemblies is ₱5. Packaging is
    /// deliberately left out of the base: what these three pay for is making the recipe, not wrapping it.
    /// </summary>
    public static decimal CalculateCostFromSubAssemblyPercent(decimal subAssemblyCostPerPiece, decimal percent) =>
        subAssemblyCostPerPiece * percent / 100m;

    /// <summary>
    /// Cost-plus pricing: margin is a markup on cost (BaseCost × (1 + Margin%)), and VAT is
    /// added on top of that pre-VAT price (× (1 + VAT%)) as a separate step — never combined
    /// into one formula, so each input maps to exactly one step a shop owner can check by hand.
    /// The base cost is the materials — the sub-assemblies plus the packaging — plus the wastage, labor,
    /// overhead and administrative costs, each of those a percentage of the sub-assemblies alone
    /// (CalculateCostFromSubAssemblyPercent). The wastage is a cost allowance for what's lost making the
    /// product (₱100 of ingredients at 3% is ₱3), added once here and never at each level of a composite;
    /// labor, overhead and administrative are worked out on the ingredients before it.
    /// </summary>
    public static (decimal BaseCost, decimal PreVatPrice, decimal SellingPrice) CalculateSellingPrice(
        decimal subAssemblyCostPerPiece,
        decimal packagingCostPerPiece,
        decimal laborPercent,
        decimal overheadPercent,
        decimal administrativePercent,
        decimal marginPercent,
        decimal vatPercent,
        decimal wastagePercent = 0m)
    {
        var wastageCost = CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece, wastagePercent);
        var laborCost = CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece, laborPercent);
        var overheadCost = CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece, overheadPercent);
        var administrativeCost = CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece, administrativePercent);
        var baseCost = subAssemblyCostPerPiece + wastageCost + packagingCostPerPiece + laborCost + overheadCost + administrativeCost;
        var preVatPrice = baseCost * (1 + marginPercent / 100m);
        var sellingPrice = preVatPrice * (1 + vatPercent / 100m);
        return (baseCost, preVatPrice, sellingPrice);
    }

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
