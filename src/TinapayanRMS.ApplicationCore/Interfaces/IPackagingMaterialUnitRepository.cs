using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IPackagingMaterialUnitRepository
{
    /// <summary>The material's alternative units, with their UnitOfMeasure, largest first.</summary>
    Task<IReadOnlyList<PackagingMaterialUnit>> GetForPackagingMaterialAsync(int packagingMaterialId, CancellationToken cancellationToken);

    /// <summary>Rejects a non-positive multiplier, the material's own base unit, a unit it already has, and a third unit (UnitConversionRules.MaxAlternativeUnits).</summary>
    Task AddAsync(PackagingMaterialUnit unit, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the unit, multiplier or description. A new multiplier moves CostPerBaseUnit when the
    /// latest price is in this unit; packaging lines entered in it are costed with the new multiplier
    /// the next time they're read.
    /// </summary>
    Task UpdateAsync(PackagingMaterialUnit unit, CancellationToken cancellationToken);

    /// <summary>Rejected while a price entry or packaging line still uses the unit.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
