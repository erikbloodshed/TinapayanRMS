namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductCloner
{
    /// <summary>
    /// Creates a new product named newProductName: a one-time snapshot of sourceProductId as saved.
    /// Its recipe links the sub-assemblies the source links, at the same piece sizes, with
    /// the source's add-ins as its own, and has its own copy of each of the source's own. Then its packaging lines as the source
    /// shows them (resolved through a packaging link, never inherited as a link; a packaging set the source
    /// links stays linked), its bulk packaging, and its costing parameters. A composite's clone is a composite holding the same products at
    /// the same quantities. One transaction: all of it, or none.
    /// </summary>
    Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a new product named newProductName as a variant of sourceProductId as saved: it links every sub-assembly
    /// the source uses, the ones it links and its own (the only way another product's own is linked), with the source's
    /// add-ins as the variant's own, at the source's piece sizes, which are the variant's own to change. The packaging is
    /// left out, neither copied nor linked. Its costing parameters, description, category and current price are copied, as
    /// CloneAsync copies them, and a composite's variant holds the same products at the same quantities. One transaction:
    /// all of it, or none.
    /// </summary>
    Task<int> CreateVariantAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken);
}
