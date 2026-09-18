using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// Reads sub-assemblies, and writes standalone ones (no owner product), which the Sub-Assemblies screen
/// makes and edits. A product's own are written with it, by IProductRepository.SaveAsync and
/// IProductCloner, never on their own.
/// </summary>
public interface ISubAssemblyRepository
{
    /// <summary>These sub-assemblies, with their role and owner.</summary>
    Task<IReadOnlyList<SubAssembly>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    /// <summary>
    /// What products can link or copy: every sub-assembly that has lines, with its role and owner,
    /// standalone ones first, then by name and owner. A product leaves out its own.
    /// </summary>
    Task<IReadOnlyList<SubAssembly>> GetLinkableAsync(CancellationToken cancellationToken);

    /// <summary>
    /// For each of the owner's sub-assemblies that another product links, the names of those products.
    /// A sub-assembly no one else uses isn't listed.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetLinkedByAsync(int ownerProductId, CancellationToken cancellationToken);

    /// <summary>The standalone sub-assemblies (no owner product), with their role, by name.</summary>
    Task<IReadOnlyList<SubAssembly>> GetStandaloneAsync(CancellationToken cancellationToken);

    /// <summary>
    /// For each standalone sub-assembly a product uses, the names of those products: a recipe's linking usages,
    /// a packaging set's packaging links. One no product uses isn't listed.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<string>>> GetStandaloneLinkedByAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds (Id 0) or updates a standalone sub-assembly's name and role, and replaces its lines (grams of
    /// ingredients), in one transaction; every product linking it follows. Refused in the Packaging role, and
    /// for a saved packaging set. Returns its id.
    /// </summary>
    Task<int> SaveStandaloneAsync(SubAssembly subAssembly, IReadOnlyList<RecipeLine> lines, CancellationToken cancellationToken);

    /// <summary>A packaging set's lines, each with its material, the material's base unit and alternative units.</summary>
    Task<IReadOnlyList<SubAssemblyPackagingLine>> GetPackagingLinesAsync(int subAssemblyId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds (Id 0) or updates a standalone packaging set — a sub-assembly in the Packaging role — and replaces its
    /// lines (packaging materials, per set, in their base unit), in one transaction; every product whose packaging
    /// links it follows. A saved recipe can't become a set. Returns its id.
    /// </summary>
    Task<int> SaveStandalonePackagingAsync(
        SubAssembly subAssembly, IReadOnlyList<(int PackagingMaterialId, decimal QtyPerSet)> lines, CancellationToken cancellationToken);

    /// <summary>Deletes a standalone sub-assembly and its lines; refused while a product links it.</summary>
    Task DeleteStandaloneAsync(int id, CancellationToken cancellationToken);
}
