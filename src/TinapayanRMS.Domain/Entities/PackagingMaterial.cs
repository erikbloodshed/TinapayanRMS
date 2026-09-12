namespace TinapayanRMS.Domain.Entities;

public class PackagingMaterial
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int PackagingCategoryId { get; set; }

    public PackagingCategory? PackagingCategory { get; set; }

    /// <summary>Piece, or a length unit for ribbon-like materials (TinapayanRMS.ApplicationCore.PackagingMaterialRules).</summary>
    public int StandardUnitId { get; set; }

    public UnitOfMeasure? StandardUnit { get; set; }

    /// <summary>The unit it's bought in.</summary>
    public int PurchaseUnitId { get; set; }

    public UnitOfMeasure? PurchaseUnit { get; set; }

    /// <summary>Standard units per purchase unit (e.g. 1000 for a 1000cm ribbon roll).</summary>
    public decimal PurchaseUnitQty { get; set; }

    /// <summary>The latest price, per purchase unit, from PackagingPriceHistory (Phase 5).</summary>
    public decimal CurrentCost { get; set; }
}
