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

    /// <summary>GetForProductAsync for several products at once, in one query: each one's usages in the order they were added.</summary>
    Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>
    /// The product's own lines on its sections, in one command: its add-ins, its extra ingredients on top of the recipes it
    /// links, each with its Ingredient, and its lines that are others of its sections (UsageSectionLine). Keyed by UsageId,
    /// not by sub-assembly: they're the product's own, so two products linking the same sub-assembly each have their own.
    /// Written by IProductRepository.SaveAsync.
    /// </summary>
    Task<UsageLines> GetLinesForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>GetLinesForProductAsync for several products at once, in one command; each line's UsageId says whose it is.</summary>
    Task<UsageLines> GetLinesForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);
}

/// <summary>A product's own lines on its sections (IProductSubAssemblyUsageRepository.GetLinesForProductAsync), each in its table's order.</summary>
public sealed record UsageLines(IReadOnlyList<UsageAddInLine> AddIns, IReadOnlyList<UsageSectionLine> SectionLines)
{
    public static UsageLines None { get; } = new([], []);
}
