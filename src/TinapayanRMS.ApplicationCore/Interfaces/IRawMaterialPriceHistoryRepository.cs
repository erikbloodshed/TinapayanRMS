using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// The log of prices a material has been costed at, added to as its brand prices move its cost
/// (IRawMaterialBrandPriceRepository). Deleting an entry doesn't reprice the material.
/// </summary>
public interface IRawMaterialPriceHistoryRepository
{
    /// <summary>The material's price entries with the unit each was priced in, latest first.</summary>
    Task<IReadOnlyList<RawMaterialPriceHistory>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
