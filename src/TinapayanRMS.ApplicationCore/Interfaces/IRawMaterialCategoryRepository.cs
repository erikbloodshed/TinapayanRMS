using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IRawMaterialCategoryRepository
{
    Task<IReadOnlyList<RawMaterialCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(RawMaterialCategory category, CancellationToken cancellationToken);

    /// <summary>Renames the category and sets its type; the type can't change while a material in it can't go in the new one.</summary>
    Task UpdateAsync(RawMaterialCategory category, CancellationToken cancellationToken);

    /// <summary>Blocked by a Restrict foreign key while any raw material is in the category.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
