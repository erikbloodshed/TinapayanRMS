namespace TinapayanRMS.Domain;

/// <summary>Only for Purchased ingredients — Made ingredients have no price history.</summary>
public class IngredientPriceHistory : IPriceHistoryEntry
{
    public int Id { get; set; }

    public int IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    /// <summary>The date on the supplier's receipt, not necessarily today.</summary>
    public DateOnly Date { get; set; }

    /// <summary>Per purchase unit (the price of one sack, one pack, …), as it appears on the receipt.</summary>
    public decimal Price { get; set; }
}
