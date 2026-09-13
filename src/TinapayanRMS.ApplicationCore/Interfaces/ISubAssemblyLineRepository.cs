using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface ISubAssemblyLineRepository
{
    /// <summary>The lines with their Ingredient, and the unit each was entered in with its UnitOfMeasure.</summary>
    Task<IReadOnlyList<SubAssemblyLine>> GetForSubAssemblyAsync(int subAssemblyId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the sub-assembly's whole line set (edited together, like an
    /// Ingredient's BOM), then recomputes its TotalWeight/TotalCost. Each line's quantity is in
    /// IngredientUnitId, one of that ingredient's own units, or in grams when it's null.
    /// </summary>
    Task SetLinesAsync(int subAssemblyId, IReadOnlyList<(int IngredientId, int? IngredientUnitId, decimal Quantity)> lines, CancellationToken cancellationToken);
}
