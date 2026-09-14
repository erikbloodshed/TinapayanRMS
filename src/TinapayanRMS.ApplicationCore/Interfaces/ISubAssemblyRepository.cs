using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// Reads sub-assemblies. They're written with their owner, by IProductRepository.SaveAsync and
/// IProductCloner, never on their own.
/// </summary>
public interface ISubAssemblyRepository
{
    /// <summary>These sub-assemblies, with their role and owner.</summary>
    Task<IReadOnlyList<SubAssembly>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    /// <summary>
    /// What products can link or copy: every sub-assembly that has lines, with its role and owner, by
    /// name then owner. A product leaves out its own.
    /// </summary>
    Task<IReadOnlyList<SubAssembly>> GetLinkableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// For each of the owner's sub-assemblies that another product links, the names of those products.
    /// A sub-assembly no one else uses isn't listed.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(int ownerProductId, CancellationToken cancellationToken);
}
