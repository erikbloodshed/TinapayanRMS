using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IProductSubAssemblyUsageRepository
{
    Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Replaces the product's whole sub-assembly usage set (edited together, like a SubAssembly's lines).</summary>
    Task SetUsagesAsync(int productId, IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> usages, CancellationToken cancellationToken);
}
