namespace TinapayanRMS.Domain;

public class UnitOfMeasure
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>Whether this unit can be substituted for another length unit as a packaging line's entry unit (TinapayanRMS.ApplicationCore.ProductRules.EnsureValidEntryUnit).</summary>
    public bool IsLengthUnit { get; set; }

    /// <summary>The conversion factor to millimeters (e.g. 10 for a centimeter, 25.4 for an inch). Required when IsLengthUnit is true; must be null otherwise (TinapayanRMS.ApplicationCore.UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent).</summary>
    public decimal? MillimetersPerUnit { get; set; }
}
