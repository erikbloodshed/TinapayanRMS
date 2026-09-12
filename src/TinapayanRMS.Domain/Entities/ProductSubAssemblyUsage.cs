namespace TinapayanRMS.Domain.Entities;

/// <summary>One sub-assembly used in a product's recipe.</summary>
public class ProductSubAssemblyUsage
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    /// <summary>
    /// Grams of this sub-assembly in one finished piece. The only field stored — yield
    /// (SubAssembly.TotalWeight / WeightPerPiece) and cost per piece (SubAssembly.TotalCost
    /// × WeightPerPiece / SubAssembly.TotalWeight) are derived from this plus the
    /// sub-assembly's current totals, not stored (TinapayanRMS.ApplicationCore.ProductRules).
    /// </summary>
    public decimal WeightPerPiece { get; set; }
}
