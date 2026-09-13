namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// An alternative unit an ingredient is bought or measured in, defined in grams (1 bag = 25,000 g).
/// The gram is every ingredient's base unit, so it never has a row of its own: a price entry or
/// recipe line with no unit is in grams.
/// </summary>
public class IngredientUnit
{
    public int Id { get; set; }

    public int IngredientId { get; set; }

    public Ingredient? Ingredient { get; set; }

    public int UnitOfMeasureId { get; set; }

    public UnitOfMeasure? UnitOfMeasure { get; set; }

    /// <summary>Grams in one of this unit, always positive (TinapayanRMS.ApplicationCore.UnitConversionRules).</summary>
    public decimal GramsPerUnit { get; set; }

    /// <summary>Optional note shown beside the unit, e.g. "Standard flour bag (25 kg)".</summary>
    public string? Description { get; set; }
}
