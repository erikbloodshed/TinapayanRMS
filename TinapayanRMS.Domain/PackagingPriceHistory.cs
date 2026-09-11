namespace TinapayanRMS.Domain;

public class PackagingPriceHistory : IPriceHistoryEntry
{
    public int Id { get; set; }

    public int PackagingMaterialId { get; set; }

    public PackagingMaterial? PackagingMaterial { get; set; }

    /// <summary>The date on the supplier's receipt, not necessarily today.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Per purchase unit (the price of one sack, one pack, …), as it appears on the receipt.</summary>
    public decimal Price { get; set; }
}
