namespace TinapayanRMS.ApplicationCore.Rules;

public static class UnitOfMeasureRules
{
    /// <summary>Every unit's Name is normalized to all lowercase (e.g. "Bag" becomes "bag") on every add/update, so the picker's display text stays consistent regardless of who typed it -- corrected automatically rather than rejected.</summary>
    public static string NormalizeName(string name) => name.ToLowerInvariant();

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
