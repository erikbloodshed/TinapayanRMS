namespace TinapayanRMS.Domain.Entities;

/// <summary>A user-managed grouping for products (Breads, Cakes, Pastries, …), a list of its own apart from raw material categories.</summary>
public sealed class ProductCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The Wastage % its products are priced with in place of the shop-wide one (CostingDefaults.WastagePercent), 0 to 100;
    /// null for the shop-wide one (2026-10-29, at the user's request: wastage per category).
    /// </summary>
    public decimal? WastagePercent { get; set; }
}
