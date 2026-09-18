namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// Only for Purchased raw materials — a Made ingredient's cost comes from its BOM instead. A log of
/// the prices the material has been costed at: saving brand prices (RawMaterialBrandPrice) that move
/// its cost adds the dearest brand's price, dated that day. Deleting an entry doesn't reprice anything.
/// </summary>
public class RawMaterialPriceHistory
{
    public int Id { get; set; }

    public int RawMaterialId { get; set; }

    public RawMaterial? RawMaterial { get; set; }

    /// <summary>The day the material started being costed at this price.</summary>
    public DateOnly Date { get; set; }

    /// <summary>The unit Price is for: one of the material's own units, or null for a price per base unit.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>The brand whose price this was, if it was named.</summary>
    public string? Brand { get; set; }

    /// <summary>Per Unit (the price of one sack, one roll, …), as it appears on the receipt.</summary>
    public decimal Price { get; set; }
}
