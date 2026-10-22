using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A price change tried on the Price Simulator screen (2026-10-03): a purchased material's price per its purchase unit, typed as a
/// change in percent or as the new price, and the costs per base unit it would give the material. Nothing here is saved.
/// </summary>
public static class PriceChangeRules
{
    public const string FallTooFarText = "A price can't fall by 100% or more. Type the new price instead.";

    public const string NewPriceTooLowText = "A new price must be more than 0.";

    public const string NoPriceToChangeText = "It has no price yet to change by a percentage. Type its new price instead.";

    /// <summary>The price after a change of <paramref name="changePercent"/>: 1,250 up 10% is 1,375.</summary>
    public static decimal NewPrice(decimal currentPrice, decimal changePercent) => currentPrice * (1m + changePercent / 100m);

    /// <summary>The change from <paramref name="currentPrice"/> to <paramref name="newPrice"/>, in percent: 1,250 to 1,375 is 10%.</summary>
    public static decimal ChangePercent(decimal currentPrice, decimal newPrice) => CostingRules.PercentOf(newPrice - currentPrice, currentPrice);

    /// <summary>
    /// The price that earns the same markup on <paramref name="newBaseCost"/> as <paramref name="currentPrice"/> earns on
    /// <paramref name="baseCost"/>: the price moves by the base cost's own factor, since the markup is on the base cost and
    /// VAT goes on top (CostingRules.CalculateMarkupPercentFromPrice). A ₱10 price on a ₱6 base cost, the base cost up to
    /// ₱6.60, is ₱11. Null without a current price, or with no base cost to keep a markup on. Not rounded.
    /// </summary>
    public static decimal? PriceToKeepMarkup(decimal? currentPrice, decimal baseCost, decimal newBaseCost) =>
        currentPrice is { } price && baseCost > 0m ? price * newBaseCost / baseCost : null;

    public static void EnsureValidChange(decimal changePercent)
    {
        if (changePercent <= -100m)
        {
            throw new BusinessRuleException(FallTooFarText);
        }
    }

    public static void EnsureValidNewPrice(decimal newPrice)
    {
        if (newPrice <= 0m)
        {
            throw new BusinessRuleException(NewPriceTooLowText);
        }
    }

    /// <summary>
    /// <paramref name="material"/>'s true and buffered costs per base unit at <paramref name="newPrice"/>, at the stored scale.
    /// <list type="bullet">
    /// <item>A material with a price (<paramref name="currentPrice"/>, the dearest brand's, which its cost comes from): both
    /// costs move by the same factor, as typing the Cost Buffer % again after a real price change would (the user's choice).
    /// A real price change alone leaves the fixed buffered cost where it was.</item>
    /// <item>One with no price yet: priced as its first price would be, the buffer percent on top of the cost
    /// (RawMaterialRules.ApplyBrandPrices).</item>
    /// </list>
    /// </summary>
    /// <param name="baseUnitsPerPriceUnit">The base units in the unit the price is for: 25,000 for a 25 kg sack of flour.</param>
    public static (decimal Cost, decimal BufferedCost) CostsAfter(RawMaterial material, decimal? currentPrice, decimal newPrice, decimal baseUnitsPerPriceUnit)
    {
        if (currentPrice is > 0m)
        {
            var factor = newPrice / currentPrice.Value;
            return (RawMaterialRules.StoredCost(material.CostPerBaseUnit * factor), RawMaterialRules.StoredCost(material.BufferedCostPerBaseUnit * factor));
        }

        var cost = UnitConversionRules.CostPerBaseUnit(newPrice, baseUnitsPerPriceUnit);
        return (RawMaterialRules.StoredCost(cost), RawMaterialRules.StoredCost(RawMaterialRules.BufferedCost(cost, material.CostBufferPercent)));
    }
}
