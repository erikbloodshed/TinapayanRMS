using TinapayanRMS.Domain.Interfaces;

namespace TinapayanRMS.Domain.Entities;

public class PackagingPriceHistory : IPriceHistoryEntry
{
    public int Id { get; set; }

    public int PackagingMaterialId { get; set; }

    public PackagingMaterial? PackagingMaterial { get; set; }

    /// <summary>The date on the supplier's receipt, not necessarily today.</summary>
    public DateOnly Date { get; set; }

    /// <summary>The unit Price is for: one of the material's own units, or null for a price per base unit.</summary>
    public int? PackagingMaterialUnitId { get; set; }

    public PackagingMaterialUnit? PackagingMaterialUnit { get; set; }

    /// <summary>Per PackagingMaterialUnit (the price of one roll, one pack, …), as it appears on the receipt.</summary>
    public decimal Price { get; set; }
}
