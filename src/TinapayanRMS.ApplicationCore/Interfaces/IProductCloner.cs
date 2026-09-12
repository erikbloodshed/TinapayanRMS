namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductCloner
{
    /// <summary>
    /// Creates a new, fully independent product named newProductName: a one-time snapshot
    /// of sourceProductId's currently-effective usages, packaging lines, and costing
    /// parameters (resolved through any tab links the source has). The new product starts
    /// with no tab links of its own — it never inherits a live link, only the data.
    /// </summary>
    Task<int> CloneAsync(int sourceProductId, string newProductName, CancellationToken cancellationToken);
}
