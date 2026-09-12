namespace TinapayanRMS.ApplicationCore;

public static class UnitOfMeasureRules
{
    /// <summary>Every unit's Name must be all lowercase (e.g. "bag", not "Bag"), so the picker's display text stays consistent regardless of who added the unit.</summary>
    public static void EnsureNameIsLowercase(string name)
    {
        if (name != name.ToLowerInvariant())
        {
            throw new InvalidOperationException("Unit name must be all lowercase.");
        }
    }

    /// <summary>A length unit needs a positive conversion factor; a non-length unit must not carry a stray one.</summary>
    public static void EnsureLengthUnitFieldsAreConsistent(bool isLengthUnit, decimal? millimetersPerUnit)
    {
        if (isLengthUnit && millimetersPerUnit is not (> 0))
        {
            throw new InvalidOperationException("A length unit needs a positive conversion factor (millimeters per unit).");
        }

        if (!isLengthUnit && millimetersPerUnit is not null)
        {
            throw new InvalidOperationException("Only a length unit has a conversion factor.");
        }
    }
}
