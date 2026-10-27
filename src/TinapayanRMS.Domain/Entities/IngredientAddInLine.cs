namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One of a made ingredient's own extra ingredients on top of the sub-assembly it's made from (RawMaterial.SubAssemblyId),
/// in grams, as a product's linked usage has UsageAddInLine (2026-10-27). It belongs to the made ingredient, not to the
/// sub-assembly: two made ingredients made from the same base each keep their own, and neither changes the base itself.
/// The made ingredient's bill of materials is the sub-assembly's lines and these (IngredientBomLine).
/// </summary>
public class IngredientAddInLine
{
    public int Id { get; set; }

    /// <summary>The made ingredient this add-in belongs to.</summary>
    public int IngredientId { get; set; }

    public RawMaterial? Ingredient { get; set; }

    /// <summary>The ingredient added, purchased, made or linked, but never one the sub-assembly already has.</summary>
    public int AddInIngredientId { get; set; }

    public RawMaterial? AddInIngredient { get; set; }

    /// <summary>The amount, always in grams: recipe lines have no unit of their own.</summary>
    public decimal Grams { get; set; }

    /// <summary>
    /// Where the line sits in the made ingredient's add-ins table, from 0: the user orders the lines by dragging them, and
    /// every save writes each one's position. Lines are read in this order, then by Id.
    /// </summary>
    public int SortOrder { get; set; }
}
