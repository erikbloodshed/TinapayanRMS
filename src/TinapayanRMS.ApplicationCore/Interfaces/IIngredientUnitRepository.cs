using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IIngredientUnitRepository
{
    /// <summary>The ingredient's alternative units, with their UnitOfMeasure, largest first.</summary>
    Task<IReadOnlyList<IngredientUnit>> GetForIngredientAsync(int ingredientId, CancellationToken cancellationToken);

    /// <summary>Rejects a non-positive multiplier, a unit the ingredient already has, and a third unit (UnitConversionRules.MaxAlternativeUnits).</summary>
    Task AddAsync(IngredientUnit unit, CancellationToken cancellationToken);

    /// <summary>
    /// Changes the unit, multiplier or description. A new multiplier flows into everything
    /// priced or measured in this unit: the ingredient's CostPerGram when its latest price is
    /// in this unit, the grams of every recipe line entered in it, and on through the cost cascade.
    /// </summary>
    Task UpdateAsync(IngredientUnit unit, CancellationToken cancellationToken);

    /// <summary>Rejected while a price entry or recipe line still uses the unit.</summary>
    Task DeleteAsync(int id, CancellationToken cancellationToken);
}
