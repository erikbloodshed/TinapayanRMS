namespace TinapayanRMS.Domain.Entities;

/// <summary>One ingredient composing a sub-assembly.</summary>
public class SubAssemblyLine
{
    public int Id { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    public int IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    /// <summary>The amount as entered, in IngredientUnit, or in grams when there's no unit.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The unit Quantity was entered in: one of the ingredient's own units, or null for grams.</summary>
    public int? IngredientUnitId { get; set; }

    public IngredientUnit? IngredientUnit { get; set; }

    /// <summary>
    /// Quantity in grams, which every weight and cost total uses. Kept in step with the unit's
    /// multiplier when that's corrected (TinapayanRMS.Infrastructure IngredientUnitRepository).
    /// </summary>
    public decimal Grams { get; set; }
}
