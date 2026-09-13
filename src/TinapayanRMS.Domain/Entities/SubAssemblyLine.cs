namespace TinapayanRMS.Domain.Entities;

/// <summary>One ingredient (a raw material of the Ingredient type) composing a sub-assembly.</summary>
public class SubAssemblyLine
{
    public int Id { get; set; }

    public int SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    public int IngredientId { get; set; }

    public RawMaterial? Ingredient { get; set; }

    /// <summary>The amount as entered, in Unit, or in grams when there's no unit.</summary>
    public decimal Quantity { get; set; }

    /// <summary>The unit Quantity was entered in: one of the ingredient's own units, or null for grams.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>
    /// Quantity in grams, which every weight and cost total uses. Kept in step with the unit's
    /// multiplier when that's corrected (TinapayanRMS.Infrastructure RawMaterialUnitRepository).
    /// </summary>
    public decimal Grams { get; set; }
}
