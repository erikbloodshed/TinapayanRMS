using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IRawMaterialRepository
{
    /// <summary>Every raw material, both types, with its category, base unit and alternative units, by name.</summary>
    Task<IReadOnlyList<RawMaterial>> GetAllAsync(CancellationToken cancellationToken);

    Task<RawMaterial?> GetByIdAsync(int id, CancellationToken cancellationToken);

    /// <summary>Rejects fields that don't fit the material's type or source (RawMaterialRules).</summary>
    Task AddAsync(RawMaterial material, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the name, category, weight loss percent and (packaging only, while nothing is measured
    /// in it yet) base unit. The type and source are fixed once created.
    /// </summary>
    Task UpdateAsync(RawMaterial material, CancellationToken cancellationToken);

    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
