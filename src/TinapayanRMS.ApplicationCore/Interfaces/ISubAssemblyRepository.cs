using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A sub-assembly filed under another folder of the Sub-Assemblies list: the role it takes from now on, which has to be of
/// its own kind (the Packaging role for a packaging set, any other for a recipe).
/// </summary>
public sealed record SubAssemblyMove(int SubAssemblyId, int RoleId);

/// <summary>
/// Reads sub-assemblies, and writes them as the Sub-Assemblies screen edits them. A product's new recipe
/// sections are first written as sub-assemblies by IProductRepository.SaveAsync.
/// </summary>
public interface ISubAssemblyRepository
{
    /// <summary>These sub-assemblies, with their role.</summary>
    Task<IReadOnlyList<SubAssembly>> GetByIdsAsync(IReadOnlyCollection<int> ids, CancellationToken cancellationToken);

    /// <summary>What products can link or copy: every sub-assembly that has lines, with its role, by name.</summary>
    Task<IReadOnlyList<SubAssembly>> GetLinkableAsync(CancellationToken cancellationToken);

    /// <summary>Every sub-assembly, with its role, by name.</summary>
    Task<IReadOnlyList<SubAssembly>> GetStandaloneAsync(CancellationToken cancellationToken);

    /// <summary>
    /// For each sub-assembly a product uses, those products, by name: a recipe's linking usages,
    /// a packaging set's packaging links. One no product uses isn't listed.
    /// </summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<LinkingProduct>>> GetStandaloneLinkedByAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Adds (Id 0) or updates a sub-assembly's name and role, and replaces its lines (grams of
    /// ingredients), in one transaction; every product linking it follows. Refused in the Packaging role, and
    /// for a saved packaging set. Returns its id.
    /// </summary>
    Task<int> SaveStandaloneAsync(SubAssembly subAssembly, IReadOnlyList<RecipeLine> lines, CancellationToken cancellationToken);

    /// <summary>A packaging set's lines, each with its material, the material's base unit and alternative units.</summary>
    Task<IReadOnlyList<SubAssemblyPackagingLine>> GetPackagingLinesAsync(int subAssemblyId, CancellationToken cancellationToken);

    /// <summary>
    /// Adds (Id 0) or updates a packaging set — a sub-assembly in the Packaging role — and replaces its
    /// lines (packaging materials, per set, each in <c>UnitId</c>, one of that material's own units, or in its base
    /// unit when that's null), in one transaction; every product whose packaging links it follows. A saved recipe
    /// can't become a set. Returns its id.
    /// </summary>
    Task<int> SaveStandalonePackagingAsync(
        SubAssembly subAssembly, IReadOnlyList<(int PackagingMaterialId, int? UnitId, decimal QtyPerSet)> lines, CancellationToken cancellationToken);

    /// <summary>
    /// Gives each sub-assembly another role, all in one transaction, and changes nothing else: the Sub-Assemblies list's
    /// drag and Move To. Each is checked as Save checks a new role (a recipe stays out of the Packaging role and a set in
    /// it, every recipe linking it can still tell its sections apart, a composite linking it keeps to product-owned roles),
    /// and the first refusal moves none. A role costs nothing, so nothing cascades.
    /// </summary>
    Task MoveAsync(IReadOnlyCollection<SubAssemblyMove> moves, CancellationToken cancellationToken);

    /// <summary>Renames a sub-assembly and changes nothing else, checked as Save checks a new name.</summary>
    Task RenameAsync(int id, string name, CancellationToken cancellationToken);

    /// <summary>Deletes a sub-assembly and its lines; refused while a product links it or a made ingredient is made from it.</summary>
    Task DeleteStandaloneAsync(int id, CancellationToken cancellationToken);

    /// <summary>For each sub-assembly a made ingredient is made from, those made ingredients, by name. One none is made from isn't listed.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<MadeIngredientUse>>> GetMadeIngredientsBySubAssemblyAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Make In-House Ingredient on the Sub-Assemblies screen: a new made ingredient (<paramref name="made"/>) made from this
    /// recipe, with no add-ins and no weight loss, in one transaction. The recipe stays here as it is, and the
    /// products linking it are left alone. Refused for a packaging set, a product's own, and a name another raw material
    /// has. Returns the made ingredient's id.
    /// </summary>
    Task<int> AddMadeIngredientAsync(int id, MadeIngredientDraft made, CancellationToken cancellationToken);
}

/// <summary>A made ingredient made from a sub-assembly, for its Consumer: one tile each, which opens it on Raw Materials.</summary>
public sealed record MadeIngredientUse(int Id, string Name);
