namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One sellable pack of a product — "Pack of 8", "Pack of 12". The pieces are the same whatever the
/// pack; only how many go in, and the box they go in, differ. A pack's cost is its pieces' share of
/// the product's cost per piece plus its own packaging lines (the ones whose
/// <see cref="PackagingLine.PackId"/> is this pack), and its price comes from the product's own
/// <see cref="CostingParameters"/> — a pack has no percentages of its own.
/// </summary>
public class ProductPack
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>What the pack is called on the price list, e.g. "Pack of 8". Unique within the product.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>How many finished pieces go in one pack.</summary>
    public int PiecesPerPack { get; set; }

    /// <summary>This pack's own packaging lines — its box, label and ribbon, never the wrapper around each piece.</summary>
    public ICollection<PackagingLine> Lines { get; } = [];
}
