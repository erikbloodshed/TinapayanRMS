namespace TinapayanRMS.Domain.Entities;

/// <summary>One component of a Made ingredient's bill of materials.</summary>
public class IngredientBomLine
{
    public int Id { get; set; }

    /// <summary>The made ingredient this line belongs to.</summary>
    public int IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    /// <summary>The ingredient consumed by this line — purchased or made, but never the same ingredient as IngredientId.</summary>
    public int ComponentIngredientId { get; set; }

    public Ingredient? ComponentIngredient { get; set; }

    public decimal Grams { get; set; }
}
