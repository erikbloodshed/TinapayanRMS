namespace TinapayanRMS.ApplicationCore.Rules;

public static class UnitOfMeasureRules
{
    /// <summary>Every unit's Name is normalized to all lowercase (e.g. "Bag" becomes "bag") on every add/update, so the picker's display text stays consistent regardless of who typed it -- corrected automatically rather than rejected. Spaces at either end go too: PostgreSQL's collation counts one at the end, so "bag " would get past the unique index beside "bag".</summary>
    public static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    /// <summary>A unit's Abbreviation as it's saved: its case is the user's ("Kg" and "kg" are one abbreviation to the unique index), but spaces at either end go, as a name's do.</summary>
    public static string NormalizeAbbreviation(string abbreviation) => abbreviation.Trim();
}
