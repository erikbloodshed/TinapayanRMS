using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IIngredientBomLineRepository
{
    /// <summary>The lines with their component, and the unit each was entered in with its UnitOfMeasure.</summary>
    Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the made ingredient's whole BOM with these lines (a BOM is edited as
    /// a set, not line by line), then recomputes its CostPerGram and ripples the
    /// change to any other made ingredient that depends on it. Each line's quantity is in
    /// IngredientUnitId, one of that component's own units, or in grams when it's null.
    /// </summary>
    Task SetLinesAsync(int ingredientId, IReadOnlyList<(int ComponentIngredientId, int? IngredientUnitId, decimal Quantity)> lines, CancellationToken cancellationToken);
}
