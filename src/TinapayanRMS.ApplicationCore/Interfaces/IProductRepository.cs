using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductRepository
{
    Task<IReadOnlyList<Product>> GetAllAsync(CancellationToken cancellationToken);

    Task<Product?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Adds the product and stamps its UpdatedAt.</summary>
    Task AddAsync(Product product, CancellationToken cancellationToken);

    /// <summary>Changes the name and description, and stamps UpdatedAt.</summary>
    Task UpdateAsync(Product product, CancellationToken cancellationToken);

    /// <summary>
    /// Saves everything the Products screen edits in one transaction, so nothing is saved unless every
    /// part passes: the product itself (added when its Id is 0, otherwise updated as UpdateAsync does),
    /// the usages of each role that changed, its packaging lines and its costing parameters, each checked
    /// as their own repositories check them. Returns the product's id.
    /// </summary>
    /// <param name="product">Only its Id, Name and Description are read; it's never given an id.</param>
    /// <param name="changedRoles">
    /// Each role (tab) whose usage set changed, with its complete new set, replacing that role's rows as
    /// IProductSubAssemblyUsageRepository.SetUsagesForRoleAsync does. A role left out keeps its rows.
    /// </param>
    /// <param name="packagingLines">The new packaging lines, as IPackagingLineRepository.SetLinesAsync takes them, or null to leave them as they are.</param>
    /// <param name="costing">The new costing parameters (only their cost and percentage fields are read), or null to leave them as they are.</param>
    Task<int> SaveAsync(
        Product product,
        IReadOnlyList<(int SubAssemblyRoleId, IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> Usages)> changedRoles,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)>? packagingLines,
        CostingParameters? costing,
        CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
