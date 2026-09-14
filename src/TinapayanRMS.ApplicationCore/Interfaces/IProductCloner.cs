namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductCloner
{
    /// <summary>
    /// Creates a new, fully independent product named newProductName: a one-time snapshot of
    /// sourceProductId as saved. Every sub-assembly the source uses, its own or linked, becomes the
    /// clone's own copy (name, role, lines and piece size); its packaging lines as the source shows them
    /// (resolved through a packaging link); and its costing parameters. The clone links nothing — it
    /// never inherits a live link, only the data. One transaction: all of it, or none.
    /// </summary>
    Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken);
}
