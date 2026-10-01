using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IRawMaterialUnitRepository
{
    /// <summary>The material's alternative units, with their UnitOfMeasure, largest first.</summary>
    Task<IReadOnlyList<RawMaterialUnit>> GetForRawMaterialAsync(int rawMaterialId, CancellationToken cancellationToken);

    /// <summary>
    /// Rejects a non-positive multiplier, a packaging material's own base unit, and a unit the material
    /// already has. There's no limit on how many alternative units a material has.
    /// </summary>
    Task AddAsync(RawMaterialUnit unit, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the unit, multiplier or description. A new multiplier flows into everything priced or
    /// measured in this unit: the material's CostPerBaseUnit when its latest price is in this unit,
    /// and for an ingredient the grams of every recipe line entered in it, on through the cost
    /// cascade. Packaging lines entered in it are costed with the new multiplier the next time
    /// they're read.
    /// </summary>
    Task UpdateAsync(RawMaterialUnit unit, CancellationToken cancellationToken);

    /// <summary>Rejected while a price entry, recipe line or packaging line still uses the unit.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
