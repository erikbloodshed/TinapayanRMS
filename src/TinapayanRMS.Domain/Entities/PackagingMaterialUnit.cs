namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An alternative unit a packaging material is bought or used in, defined in the material's base
/// unit (1 roll = 1,000 cm, 1 pack = 100 pieces). The base unit never has a row of its own: a price
/// entry or packaging line with no unit is in the base unit.
/// </summary>
public class PackagingMaterialUnit
{
    public int Id { get; set; }

    public int PackagingMaterialId { get; set; }

    public PackagingMaterial? PackagingMaterial { get; set; }

    /// <summary>Never the material's own base unit (TinapayanRMS.ApplicationCore.PackagingMaterialRules).</summary>
    public int UnitOfMeasureId { get; set; }

    public UnitOfMeasure? UnitOfMeasure { get; set; }

    /// <summary>Base units in one of this unit, always positive.</summary>
    public decimal BaseUnitsPerUnit { get; set; }

    /// <summary>Optional note shown beside the unit, e.g. "Satin ribbon roll (10 m)".</summary>
    public string? Description { get; set; }
}
