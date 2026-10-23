using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One sub-assembly used in a product's recipe: one the product owns, or a link to another product's.
/// Each product sizes its pieces itself, so two products can use the same batch differently.
/// </summary>
public class ProductSubAssemblyUsage
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    /// <summary>Which figure the user typed: the weight per piece or the yield.</summary>
    public UsageAuthoredField AuthoredField { get; set; }

    /// <summary>
    /// The typed figure: grams per piece, or pieces per batch. Only this is stored; the other is worked
    /// out from the batch's total weight on read (ProductRules.ResolvePieceSize), so the
    /// typed one stays put when the batch changes.
    /// </summary>
    public decimal AuthoredValue { get; set; }
}
