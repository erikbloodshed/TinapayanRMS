using TinapayanRMS.Domain.Interfaces;

namespace TinapayanRMS.Domain.Entities;

/// <summary>Only for Purchased raw materials — a Made ingredient's cost comes from its BOM instead.</summary>
public class RawMaterialPriceHistory : IPriceHistoryEntry
{
    public int Id { get; set; }

    public int RawMaterialId { get; set; }

    public RawMaterial? RawMaterial { get; set; }

    /// <summary>The date on the supplier's receipt, not necessarily today.</summary>
    public DateOnly Date { get; set; }

    /// <summary>The unit Price is for: one of the material's own units, or null for a price per base unit.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>Per Unit (the price of one sack, one roll, …), as it appears on the receipt.</summary>
    public decimal Price { get; set; }
}
