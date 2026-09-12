namespace TinapayanRMS.ApplicationCore;

public static class CostingRules
{
    public static void EnsureValidParameters(decimal laborCost, decimal overheadCost, decimal marginPercent, decimal vatPercent)
    {
        if (laborCost < 0 || overheadCost < 0)
        {
            throw new InvalidOperationException("Labor and overhead cost can't be negative.");
        }

        if (marginPercent < 0 || vatPercent < 0)
        {
            throw new InvalidOperationException("Margin and VAT percentages can't be negative.");
        }
    }

    /// <summary>
    /// Cost-plus pricing: margin is a markup on cost (BaseCost × (1 + Margin%)), and VAT is
    /// added on top of that pre-VAT price (× (1 + VAT%)) as a separate step — never combined
    /// into one formula, so each of the four inputs maps to exactly one multiplication a shop
    /// owner can check by hand.
    /// </summary>
    public static (decimal BaseCost, decimal PreVatPrice, decimal SellingPrice) CalculateSellingPrice(
        decimal materialCostPerPiece, decimal laborCost, decimal overheadCost, decimal marginPercent, decimal vatPercent)
    {
        var baseCost = materialCostPerPiece + laborCost + overheadCost;
        var preVatPrice = baseCost * (1 + marginPercent / 100m);
        var sellingPrice = preVatPrice * (1 + vatPercent / 100m);
        return (baseCost, preVatPrice, sellingPrice);
    }
}
