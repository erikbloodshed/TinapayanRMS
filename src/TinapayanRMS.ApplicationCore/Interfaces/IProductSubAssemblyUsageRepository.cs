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

    /// <summary>
    /// The product's add-ins: its own extra ingredients on top of the recipes it links, each with its
    /// Ingredient, in one query. Keyed by UsageId, not by sub-assembly — a product's add-ins are its own,
    /// so two products linking the same sub-assembly each have their own set. Written by
    /// IProductRepository.SaveAsync.
    /// </summary>
    Task<IReadOnlyList<UsageAddInLine>> GetAddInsForProductAsync(int productId, CancellationToken cancellationToken);
}
