namespace TinapayanRMS.Domain.Entities;

public class PackagingMaterial
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int PackagingCategoryId { get; set; }

    public PackagingCategory? PackagingCategory { get; set; }

    /// <summary>
    /// The unit its quantities and cost are tracked in, chosen per material: typically a piece for
    /// boxes, a length for ribbon. Fixed once the material has alternative units, prices or
    /// packaging lines, which are all measured against it.
    /// </summary>
    public int BaseUnitId { get; set; }

    public UnitOfMeasure? BaseUnit { get; set; }

    /// <summary>
    /// Stored rather than computed, like Ingredient.CostPerGram: the latest price divided by the base
    /// units in that price's unit. 0 until priced.
    /// </summary>
    public decimal CostPerBaseUnit { get; set; }

    /// <summary>The units it's bought or used in besides its base unit.</summary>
    public List<PackagingMaterialUnit> Units { get; set; } = [];
}
