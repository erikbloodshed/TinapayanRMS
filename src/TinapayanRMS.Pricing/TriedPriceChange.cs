using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A new price tried for a purchased material, as the Price Simulator tries it: typed as the price itself or as a change
/// in percent from the material's current price, with the costs per base unit the material would have at it.
/// </summary>
/// <param name="Unit">The unit the prices are per, with the material's current price in it.</param>
/// <param name="ChangePercent">How far the price moves; null when there's no current price to move from.</param>
/// <param name="Cost">The material's true cost per base unit at <paramref name="NewPrice"/>.</param>
/// <param name="BufferedCost">Its buffered cost per base unit at <paramref name="NewPrice"/>.</param>
public sealed record TriedPriceChange(RawMaterial Material, PurchaseUnit Unit, decimal? ChangePercent, decimal NewPrice, decimal Cost, decimal BufferedCost)
{
    /// <summary>
    /// The change typed as <paramref name="changePercent"/> (when <paramref name="percentTyped"/>) or as
    /// <paramref name="newPrice"/>, the other worked out from the current price; null while there's no new price to try.
    /// Refused: a percentage on a material with no price yet, a fall of 100% or more, and a new price of 0 or less.
    /// </summary>
    public static TriedPriceChange? Of(RawMaterial material, PurchaseUnit unit, decimal? changePercent, decimal? newPrice, bool percentTyped)
    {
        if (percentTyped)
        {
            if (changePercent is not { } percent)
            {
                return null;
            }

            if (unit.Price is not { } current)
            {
                throw new BusinessRuleException($"{material.Name}: {PriceChangeRules.NoPriceToChangeText}");
            }

            PriceChangeRules.EnsureValidChange(percent);
            newPrice = PriceChangeRules.NewPrice(current, percent);
        }
        else
        {
            changePercent = unit.Price is > 0m && newPrice is { } typed ? PriceChangeRules.ChangePercent(unit.Price.Value, typed) : null;
        }

        if (newPrice is not { } price)
        {
            return null;
        }

        PriceChangeRules.EnsureValidNewPrice(price);
        var (cost, bufferedCost) = PriceChangeRules.CostsAfter(material, unit.Price, price, unit.BaseUnitsPerUnit);
        return new TriedPriceChange(material, unit, changePercent, price, cost, bufferedCost);
    }
}
