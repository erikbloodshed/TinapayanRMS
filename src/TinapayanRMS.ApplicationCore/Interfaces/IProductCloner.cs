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
}
