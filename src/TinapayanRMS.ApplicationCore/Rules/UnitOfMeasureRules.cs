namespace TinapayanRMS.ApplicationCore.Rules;

public static class UnitOfMeasureRules
{
    /// <summary>Every unit's Name is normalized to all lowercase (e.g. "Bag" becomes "bag") on every add/update, so the picker's display text stays consistent regardless of who typed it -- corrected automatically rather than rejected.</summary>
    public static string NormalizeName(string name) => name.ToLowerInvariant();
}
