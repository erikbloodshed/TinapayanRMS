using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IPackagingMaterialRepository
{
    Task<IReadOnlyList<PackagingMaterial>> GetAllAsync(CancellationToken cancellationToken);

    Task<PackagingMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken);

    Task AddAsync(PackagingMaterial packagingMaterial, CancellationToken cancellationToken);

    Task UpdateAsync(PackagingMaterial packagingMaterial, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
