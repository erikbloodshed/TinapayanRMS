namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductCloner
{
    /// <summary>
    /// Creates a new product named newProductName: a one-time snapshot of sourceProductId as saved.
    /// Every sub-assembly the source uses, its own or another product's, becomes the clone's own copy
    /// (name, role, lines and piece size); a standalone one stays linked, at the same piece size. Then its
    /// packaging lines as the source shows them (resolved through a packaging link, never inherited as a
    /// link), and its costing parameters. One transaction: all of it, or none.
    /// </summary>
    Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new product named newProductName as a variant of sourceProductId as saved: it links what the source's
    /// recipe uses instead of copying it, so it follows every change made there. The source's own sub-assemblies are
    /// linked from the source; one the source links (another product's or a standalone one) is linked where it's kept,
    /// since links don't chain, with the source's add-ins as the variant's own. All at the source's piece sizes, which
    /// are the variant's own to change. Its per-piece packaging links the source's when the source has any (its own
    /// lines or a link). Its packs, costing parameters, description, category and current price are copied, as
    /// CloneAsync copies them. One transaction: all of it, or none.
    /// </summary>
    Task<int> CreateVariantAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken);
}
