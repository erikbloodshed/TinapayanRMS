namespace TinapayanRMS.Domain.Entities;

/// <summary>One component of a Made ingredient's bill of materials. Both ends are raw materials of the Ingredient type.</summary>
public class IngredientBomLine
{
    public int Id { get; set; }

    /// <summary>The made ingredient this line belongs to.</summary>
    public int IngredientId { get; set; }

    public RawMaterial? Ingredient { get; set; }

    /// <summary>The ingredient consumed by this line — purchased or made, but never the same ingredient as IngredientId.</summary>
    public int ComponentIngredientId { get; set; }

    public RawMaterial? ComponentIngredient { get; set; }

    /// <summary>The amount as entered, in Unit, or in grams when there's no unit.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The unit Quantity was entered in: one of the component's own units, or null for grams.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>
    /// Quantity in grams, which the made ingredient's cost uses. Kept in step with the unit's
    /// multiplier when that's corrected (TinapayanRMS.Infrastructure RawMaterialUnitRepository).
    /// </summary>
    public decimal Grams { get; set; }
}
