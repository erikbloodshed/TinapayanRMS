using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IIngredientBomLineRepository
{
    Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the made ingredient's whole BOM with these lines (a BOM is edited as
    /// a set, not line by line), then recomputes its CostPerGram and ripples the
    /// change to any other made ingredient that depends on it.
    /// </summary>
    Task SetLinesAsync(int ingredientId, IReadOnlyList<(int ComponentIngredientId, decimal Grams)> lines, CancellationToken cancellationToken);
}
