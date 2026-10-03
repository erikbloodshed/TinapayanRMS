using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IRawMaterialBrandPriceRepository
{
    /// <summary>A purchased material's brand prices with the unit each is for, in the order they were added.</summary>
    Task<IReadOnlyList<RawMaterialBrandPrice>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken);

    /// <summary>Every purchased material's brand prices with the unit each is for, by material, each's in the order they were added.</summary>
    Task<IReadOnlyList<RawMaterialBrandPrice>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Replaces a purchased material's brand prices with <paramref name="brandPrices"/> (only their Brand,
    /// UnitId and Price are read) and reprices it at the dearest per base unit. A new cost is logged in
    /// price history, dated today, and cascaded.
    /// </summary>
    Task SetAsync(int rawMaterialId, IReadOnlyList<RawMaterialBrandPrice> brandPrices, CancellationToken cancellationToken);
}
