using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IPackagingPriceHistoryRepository
{
    Task<IReadOnlyList<PackagingPriceHistory>> GetForPackagingMaterialAsync(int packagingMaterialId, CancellationToken cancellationToken);

    Task AddAsync(PackagingPriceHistory entry, CancellationToken cancellationToken);

    Task UpdateAsync(PackagingPriceHistory entry, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
