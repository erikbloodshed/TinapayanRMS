namespace TinapayanRMS.Domain.Entities;

public class Product
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>A short description shown under the product's name, e.g. "Soft bread with a creamy filling". Optional.</summary>
    public string? Description { get; set; }

    /// <summary>What the Products list groups it under (Breads, Cakes, …). Optional: null for an uncategorized product.</summary>
    public int? CategoryId { get; set; }

    public ProductCategory? Category { get; set; }

    /// <summary>
    /// What the shop charges for one piece now, with VAT, as entered: set beside the computed selling price to
    /// see the markup it actually earns. Optional: null when not entered.
    /// </summary>
    public decimal? CurrentPrice { get; set; }

    /// <summary>
    /// When the product was last saved, in local time: set each time it's added or updated, and for a
    /// clone when it's made. Null for a product not saved since this was added.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }
}
