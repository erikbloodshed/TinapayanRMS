namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// Conversions between a raw material's alternative units and its base unit (grams for an
/// ingredient). A null factor means the quantity or price is already in the base unit.
/// </summary>
public static class UnitConversionRules
{
    /// <summary>The most alternative units one raw material can have, besides its base unit.</summary>
    public const int MaxAlternativeUnits = 2;

    /// <summary>An item already holding <see cref="MaxAlternativeUnits"/> alternative units can't take another.</summary>
    public static void EnsureCanAddAlternativeUnit(int existingUnitCount)
    {
        if (existingUnitCount >= MaxAlternativeUnits)
        {
            throw new InvalidOperationException($"An item can have at most {MaxAlternativeUnits} alternative units. Remove one before adding another.");
        }
    }

    /// <summary>A quantity in an alternative unit, in base units: 2 bags of 25,000 g are 50,000 g.</summary>
    public static decimal ToBaseQuantity(decimal quantity, decimal? baseUnitsPerUnit) =>
        quantity * (baseUnitsPerUnit ?? 1m);

    /// <summary>A price for one alternative unit, per base unit: ₱1,250 a 25,000 g bag is ₱0.05 a gram.</summary>
    public static decimal CostPerBaseUnit(decimal price, decimal? baseUnitsPerUnit) =>
        price / (baseUnitsPerUnit ?? 1m);

    /// <summary>An alternative unit's multiplier (base units in one of it) must be positive.</summary>
    public static void EnsureValidMultiplier(decimal baseUnitsPerUnit)
    {
        if (baseUnitsPerUnit <= 0)
        {
            throw new InvalidOperationException("A unit's multiplier must be positive.");
        }
    }
}
