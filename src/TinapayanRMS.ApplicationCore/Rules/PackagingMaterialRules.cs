using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class PackagingMaterialRules
{
    /// <summary>
    /// Recomputes CostPerBaseUnit from the latest price history entry: its price divided by the base
    /// units in the unit it was priced in (<paramref name="baseUnitsPerUnit"/>, null for a price per
    /// base unit). No entry reverts the material to unpriced. Call after every add/update/delete of a
    /// price entry, and after the latest entry's unit changes its multiplier.
    /// </summary>
    public static void ApplyLatestPrice(PackagingMaterial packagingMaterial, decimal? latestPrice, decimal? baseUnitsPerUnit)
    {
        packagingMaterial.CostPerBaseUnit = latestPrice is { } price
            ? UnitConversionRules.CostPerBaseUnit(price, baseUnitsPerUnit)
            : 0m;
    }

    /// <summary>The base unit is the one unit a material has without an alternative-unit row, so it can't also be one.</summary>
    public static void EnsureNotTheBaseUnit(int baseUnitId, int unitOfMeasureId)
    {
        if (baseUnitId == unitOfMeasureId)
        {
            throw new InvalidOperationException("That's already the material's base unit.");
        }
    }

    /// <summary>
    /// A material's alternative units, prices and packaging lines are all measured against its base
    /// unit, so it can only change while it has none of them.
    /// </summary>
    public static void EnsureBaseUnitCanChange(bool hasUnits, bool hasPrices, bool hasLines)
    {
        if (hasUnits || hasPrices || hasLines)
        {
            throw new InvalidOperationException(
                "The base unit can't change once the material has alternative units, prices or packaging lines, which are all measured in it.");
        }
    }
}
