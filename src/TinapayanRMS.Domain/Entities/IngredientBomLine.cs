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

    /// <summary>The amount, always in grams: recipe lines have no unit of their own.</summary>
    public decimal Grams { get; set; }

    /// <summary>
    /// Where the line sits in its BOM's table, from 0: the user orders the lines by dragging them, and every save
    /// writes each one's position. Lines are read in this order, then by Id.
    /// </summary>
    public int SortOrder { get; set; }
}
