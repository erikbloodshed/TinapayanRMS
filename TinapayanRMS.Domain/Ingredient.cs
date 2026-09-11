namespace TinapayanRMS.Domain;

public class Ingredient
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public int IngredientCategoryId { get; set; }

    public IngredientCategory? IngredientCategory { get; set; }

    /// <summary>Set at creation and never changed afterwards (TinapayanRMS.ApplicationCore.IngredientRules).</summary>
    public IngredientSource Source { get; set; }

    /// <summary>Purchased only: the unit it's bought in.</summary>
    public int? PurchaseUnitId { get; set; }

    public UnitOfMeasure? PurchaseUnit { get; set; }

    /// <summary>Purchased only: grams per purchase unit (e.g. 25000 for a 25kg sack).</summary>
    public decimal? PurchaseUnitQty { get; set; }

    /// <summary>Purchased only: the latest price, per purchase unit, from IngredientPriceHistory (Phase 5).</summary>
    public decimal? CurrentCost { get; set; }

    /// <summary>
    /// Every ingredient, whichever source. Stored rather than computed, matching
    /// SubAssembly/Product costs elsewhere in the cascade: for Purchased it's
    /// CurrentCost / PurchaseUnitQty (Phase 5), for Made it's a BOM-tree rollup
    /// (Phase 6). Defaults to 0 until that phase's logic populates it.
    /// </summary>
    public decimal CostPerGram { get; set; }
}
