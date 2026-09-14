using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IIngredientBomLineRepository
{
    /// <summary>The lines with their component.</summary>
    Task<IReadOnlyList<IngredientBomLine>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the made ingredient's whole BOM with these lines, in grams (a BOM is edited as a set, not
    /// line by line), then recomputes its CostPerBaseUnit and ripples the change to any other made
    /// ingredient that depends on it. Every component must be an ingredient
    /// (RawMaterialRules.EnsureUsableInRecipes).
    /// </summary>
    Task SetLinesAsync(int ingredientId, IReadOnlyList<(int ComponentIngredientId, decimal Grams)> lines, CancellationToken cancellationToken);
}
