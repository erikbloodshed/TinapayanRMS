using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

public class Ingredient
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int IngredientCategoryId { get; set; }

    public IngredientCategory? IngredientCategory { get; set; }

    /// <summary>Set at creation and never changed afterwards (TinapayanRMS.ApplicationCore.IngredientRules).</summary>
    public IngredientSource Source { get; set; }

    /// <summary>Made only: weight lost while making it, at least 0 and less than 100.</summary>
    public decimal? WeightLossPercent { get; set; }

    /// <summary>
    /// Every ingredient, whichever source. Stored rather than computed, matching
    /// SubAssembly/Product costs elsewhere in the cascade: for Purchased it's the latest
    /// price divided by the grams in that price's unit, for Made it's a BOM-tree rollup
    /// (Phase 6). Defaults to 0 until priced / given a BOM.
    /// </summary>
    public decimal CostPerGram { get; set; }

    /// <summary>The units it's bought or measured in besides the gram, its base unit.</summary>
    public List<IngredientUnit> Units { get; set; } = [];
}
