namespace TinapayanRMS.Domain.Entities;

/// <summary>A user-managed grouping for products (Breads, Cakes, Pastries, …), a list of its own apart from raw material categories.</summary>
public sealed class ProductCategory
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The Wastage % its products are priced with in place of the shop-wide one (CostingDefaults.WastagePercent), 0 to 100;
    /// null for the shop-wide one.
    /// </summary>
    public decimal? WastagePercent { get; set; }

    // The costing percentages its new products start with, and Apply Costing Defaults gives its products, each in place of
    // Settings' (CostingDefaults); null for Settings' own (CostingRules.DefaultCostingFor). A product keeps its own costing
    // from its first Save, as with Settings' defaults: changing these changes no saved product.

    /// <summary>In place of CostingDefaults.LaborPercent; null for it.</summary>
    public decimal? LaborPercent { get; set; }

    /// <summary>In place of CostingDefaults.OverheadPercent; null for it.</summary>
    public decimal? OverheadPercent { get; set; }

    /// <summary>In place of CostingDefaults.AdministrativePercent; null for it.</summary>
    public decimal? AdministrativePercent { get; set; }

    /// <summary>In place of CostingDefaults.MarkupPercent, a markup on cost; null for it.</summary>
    public decimal? MarkupPercent { get; set; }

    /// <summary>
    /// The Warn Below % its products are flagged with in place of Settings' (CostingDefaults.MarkupFloorPercent), 0 or
    /// more; null for Settings'. Unlike the costing percentages it reaches saved products, as the wastage does.
    /// </summary>
    public decimal? MarkupFloorPercent { get; set; }
}
