using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IRawMaterialCategoryRepository
{
    Task<IReadOnlyList<RawMaterialCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(RawMaterialCategory category, CancellationToken cancellationToken);

    Task UpdateAsync(RawMaterialCategory category, CancellationToken cancellationToken);

    /// <summary>Blocked by a Restrict foreign key while any raw material is in the category.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
