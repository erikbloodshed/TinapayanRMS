using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductSubAssemblyUsageRepository
{
    /// <summary>
    /// The product's usages, its own sub-assemblies and those it links, each with its sub-assembly, that
    /// sub-assembly's role and its owner. A usage whose sub-assembly's owner isn't the product is a link.
    /// Written by IProductRepository.SaveAsync.
    /// </summary>
    Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductAsync(int productId, CancellationToken cancellationToken);
}
