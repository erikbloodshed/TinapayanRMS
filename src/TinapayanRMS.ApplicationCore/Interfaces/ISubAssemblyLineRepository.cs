using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ISubAssemblyLineRepository
{
    Task<IReadOnlyList<SubAssemblyLine>> GetForSubAssemblyAsync(int subAssemblyId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the sub-assembly's whole line set (edited together, like an
    /// Ingredient's BOM), then recomputes its TotalWeight/TotalCost.
    /// </summary>
    Task SetLinesAsync(int subAssemblyId, IReadOnlyList<(int IngredientId, decimal Grams)> lines, CancellationToken cancellationToken);
}
