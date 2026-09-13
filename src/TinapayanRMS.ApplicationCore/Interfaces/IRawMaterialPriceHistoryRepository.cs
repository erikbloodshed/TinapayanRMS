using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IRawMaterialPriceHistoryRepository
{
    /// <summary>The material's price entries with the unit each was priced in, latest first.</summary>
    Task<IReadOnlyList<RawMaterialPriceHistory>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken);

    Task AddAsync(RawMaterialPriceHistory entry, CancellationToken cancellationToken);

    Task UpdateAsync(RawMaterialPriceHistory entry, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
