using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IPackagingCategoryRepository
{
    Task<IReadOnlyList<PackagingCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task AddAsync(PackagingCategory category, CancellationToken cancellationToken);

    Task UpdateAsync(PackagingCategory category, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
