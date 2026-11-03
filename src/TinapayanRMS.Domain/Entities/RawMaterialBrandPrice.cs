namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// What one brand of a Purchased raw material costs now: a row of the Purchase Cost Setup. A material
/// can be bought in several brands, each in its own unit, and is costed at the dearest of them per base
/// unit (TinapayanRMS.ApplicationCore.RawMaterialRules.ApplyBrandPrices). Price history only logs the
/// price that set the cost.
/// </summary>
public sealed class RawMaterialBrandPrice
{
    public int Id { get; set; }

    public int RawMaterialId { get; set; }

    public RawMaterial? RawMaterial { get; set; }

    /// <summary>Optional: a material bought in one brand needn't name it.</summary>
    public string? Brand { get; set; }

    /// <summary>The unit Price is for: one of the material's own units, or null for a price per base unit.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>Per Unit (the price of one sack, one roll, …), always positive.</summary>
    public decimal Price { get; set; }
}
