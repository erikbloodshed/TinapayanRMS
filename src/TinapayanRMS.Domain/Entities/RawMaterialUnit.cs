namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An alternative unit a raw material is bought or used in, defined in the material's base unit
/// (1 bag = 25,000 g, 1 roll = 1,000 cm). The base unit never has a row of its own: a price entry or
/// line with no unit is in the base unit, so nothing depends on a "gram" row in Units of Measure.
/// </summary>
public sealed class RawMaterialUnit
{
    public int Id { get; set; }

    public int RawMaterialId { get; set; }

    public RawMaterial? RawMaterial { get; set; }

    /// <summary>Never a packaging material's own base unit (TinapayanRMS.ApplicationCore.RawMaterialRules).</summary>
    public int UnitOfMeasureId { get; set; }

    public UnitOfMeasure? UnitOfMeasure { get; set; }

    /// <summary>Base units in one of this unit (grams, for an ingredient), always positive (TinapayanRMS.ApplicationCore.UnitConversionRules).</summary>
    public decimal BaseUnitsPerUnit { get; set; }

    /// <summary>Optional note shown beside the unit, e.g. "Standard flour bag (25 kg)".</summary>
    public string? Description { get; set; }
}
