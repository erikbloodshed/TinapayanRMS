using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IPackagingCategoryRepository
{
    Task<IReadOnlyList<PackagingCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(PackagingCategory category, CancellationToken cancellationToken);

    Task UpdateAsync(PackagingCategory category, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
