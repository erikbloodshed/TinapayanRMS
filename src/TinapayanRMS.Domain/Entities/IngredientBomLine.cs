namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One component of a made ingredient's bill of materials, as the sub-assembly it's made from (RawMaterial.SubAssemblyId)
/// and its own add-ins (IngredientAddInLine) give it: read, never stored. Both ends are raw materials of the Ingredient
/// type.
/// </summary>
public sealed class IngredientBomLine
{
    /// <summary>The made ingredient this line belongs to.</summary>
    public int IngredientId { get; set; }

    /// <summary>The ingredient consumed by this line: purchased, made or linked, but never the same ingredient as IngredientId.</summary>
    public int ComponentIngredientId { get; set; }

    public RawMaterial? ComponentIngredient { get; set; }

    /// <summary>The amount, always in grams: recipe lines have no unit of their own.</summary>
    public decimal Grams { get; set; }

    /// <summary>Where the line sits: the sub-assembly's lines in their order, then the add-ins in theirs.</summary>
    public int SortOrder { get; set; }

    /// <summary>One of the made ingredient's own add-ins, rather than a line of its sub-assembly.</summary>
    public bool IsAddIn { get; set; }
}
