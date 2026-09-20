using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// One of the product's own sub-assemblies as Save writes it: its role, which is what it's called, its gram
/// lines, and the product's piece size for it (an owner always uses its own sub-assemblies). Id is 0 for a new one.
/// </summary>
/// <param name="Name">
/// Optional: what tells it apart from others in its role, e.g. "Ube" beside "Cheese" (SubAssemblyRules.EnsureSectionsDistinct).
/// </param>
public sealed record OwnedSubAssemblyDraft(
    int Id,
    int RoleId,
    IReadOnlyList<RecipeLine> Lines,
    UsageAuthoredField AuthoredField,
    decimal AuthoredValue,
    string? Name = null);

/// <summary>A sub-assembly the product links (another product's, or a standalone one), at the product's own piece size.</summary>
public sealed record LinkedUsageDraft(int SubAssemblyId, UsageAuthoredField AuthoredField, decimal AuthoredValue)
{
    /// <summary>
    /// This product's own extra ingredients on top of the linked recipe, in grams, and never one the recipe
    /// already has (ProductRules.EnsureValidAddIns). They go into the batch, so the piece size shares out the
    /// owner's lines plus these. Empty for a usage that takes the recipe as it is.
    /// </summary>
    public IReadOnlyList<(int IngredientId, decimal Grams)> AddIns { get; init; } = [];
}

/// <summary>
/// A product's whole recipe of sub-assemblies: the ones it owns and the ones it links. Saving it replaces
/// the product's usages, adds, updates and relines its own sub-assemblies, and deletes the ones it no
/// longer lists.
/// </summary>
public sealed record RecipeDraft(IReadOnlyList<OwnedSubAssemblyDraft> Owned, IReadOnlyList<LinkedUsageDraft> Linked);

/// <summary>
/// What a product's packaging links, as Save writes it: another product's packaging, a standalone packaging
/// set (one set per sellable unit), or neither (<see cref="None"/>), when the product owns its packaging lines.
/// </summary>
public sealed record PackagingLinkDraft(int? LinkedProductId, int? LinkedSubAssemblyId)
{
    /// <summary>No link: the product's packaging is its own lines.</summary>
    public static PackagingLinkDraft None { get; } = new(null, null);
}

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Adds the product and stamps its UpdatedAt.</summary>
    Task AddAsync(Product product, CancellationToken cancellationToken);

    /// <summary>Changes the name, description, category, quantity required and current price, and stamps UpdatedAt.</summary>
    Task UpdateAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything the Products screen edits in one transaction, so nothing is saved unless every
    /// part passes: the product itself (added when its Id is 0, otherwise updated as UpdateAsync does),
    /// its recipe, its packaging lines and its costing parameters, each checked as their own repositories
    /// check them. Returns the product's id.
    /// </summary>
    /// <param name="product">Only its Id, Name, Description, CategoryId, QuantityRequired and CurrentPrice are read; it's never given an id.</param>
    /// <param name="recipe">
    /// The product's complete recipe of sub-assemblies, or null to leave it as it is. An owned
    /// sub-assembly it no longer lists is deleted, unless another product links it.
    /// </param>
    /// <param name="packagingLink">
    /// What the packaging links from now on, or null to leave the link as it is. Written before the lines, so
    /// a product unlinked here can be given its own lines in the same save. Removing a link doesn't copy the lines it
    /// showed: pass them as <paramref name="packagingLines"/>.
    /// </param>
    /// <param name="packagingLines">The new packaging lines, as IPackagingLineRepository.SetLinesAsync takes them, or null to leave them as they are.</param>
    /// <param name="costing">The new costing parameters (only their cost and percentage fields are read), or null to leave them as they are.</param>
    Task<int> SaveAsync(
        Product product,
        RecipeDraft? recipe,
        PackagingLinkDraft? packagingLink,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        CostingParameters? costing,
        CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the product with its own sub-assemblies, usages, packaging and costing. Refused while
    /// another product links one of its sub-assemblies, or links its packaging.
    /// </summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
