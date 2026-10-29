using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// Anything bought or made to produce a product: an ingredient for recipes, or a packaging material
/// for a product's packaging lines (<see cref="Type"/>). Both kinds share one table, one category
/// list, alternative units and price history; the type decides where a material can be used and
/// what its base unit is (TinapayanRMS.ApplicationCore.RawMaterialRules).
/// </summary>
public sealed class RawMaterial
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Set at creation and never changed afterwards: recipes, BOMs and packaging lines depend on it.</summary>
    public RawMaterialType Type { get; set; }

    public int CategoryId { get; set; }

    public RawMaterialCategory? Category { get; set; }

    /// <summary>Set at creation and never changed afterwards. Only an ingredient can be Made or Linked; packaging is always Purchased.</summary>
    public RawMaterialSource Source { get; set; }

    /// <summary>Made only: weight lost while making it, at least 0 and less than 100.</summary>
    public decimal? WeightLossPercent { get; set; }

    /// <summary>
    /// Made only, and required for one: the sub-assembly whose recipe it's made from (2026-10-27). The Sub-Assemblies screen
    /// is the one place the recipe is written; the made ingredient adds its own add-ins (IngredientAddInLine) and its
    /// weight loss on top. Only one kept on the Sub-Assemblies screen, and not a packaging set (SubAssemblyRules.EnsureLinkableByMadeIngredient).
    /// </summary>
    public int? SubAssemblyId { get; set; }

    public SubAssembly? SubAssembly { get; set; }

    /// <summary>
    /// Linked only: the ingredient this one is a part of, whose cost it takes a share of — egg white
    /// is linked to the egg. Its cost per gram is the linked-to one's × CostSharePercent ÷ WeightSharePercent.
    /// </summary>
    public int? LinkedToId { get; set; }

    public RawMaterial? LinkedTo { get; set; }

    /// <summary>Linked only: how much of the linked-to ingredient's weight this part is (egg white, 70%). Above 0, at most 100.</summary>
    public decimal? WeightSharePercent { get; set; }

    /// <summary>Linked only: how much of the linked-to ingredient's cost this part carries (egg white, 30%). Above 0, at most 100.</summary>
    public decimal? CostSharePercent { get; set; }

    /// <summary>
    /// Packaging only: the unit its quantities and cost are tracked in, chosen per material —
    /// typically a piece for boxes, a length for ribbon. Fixed once the material has alternative
    /// units, prices or packaging lines, which are all measured against it. Null for an ingredient,
    /// whose base unit is always the gram: recipes, BOMs and sub-assembly weights all add up grams.
    /// </summary>
    public int? BaseUnitId { get; set; }

    public UnitOfMeasure? BaseUnit { get; set; }

    /// <summary>
    /// The cost of one base unit (one gram, for an ingredient). Stored rather than computed, matching
    /// SubAssembly/Product costs elsewhere in the cascade: for Purchased it's the latest price divided
    /// by the base units in that price's unit, for Made it's a BOM-tree rollup (Phase 6), for Linked a
    /// share of the linked-to ingredient's. Defaults to 0 until priced / given a BOM.
    /// </summary>
    public decimal CostPerBaseUnit { get; set; }

    /// <summary>
    /// A cushion on top of the cost for price swings, waste and yield variation, at least 0 and at
    /// most 100. 0 makes the buffered cost the same as the true cost; a new one on the Raw Materials screen starts at
    /// Settings' default (CostingDefaults.CostBufferPercent, 10% unless changed). A Linked ingredient
    /// inherits it from the ingredient it's linked to, and the cascade keeps it in step. A Made ingredient is
    /// always 0: its components are already buffered, so it adds none of its own.
    /// </summary>
    public decimal CostBufferPercent { get; set; }

    /// <summary>
    /// Purchased only: the buffer percent last typed for it, 0 to 100 (2026-10-29), which <see cref="CostBufferPercent"/> drifts
    /// from as prices change, since the buffered cost stays fixed. What Re-apply Target puts back on top of the dearest brand.
    /// Null for one saved before it was kept, which takes <see cref="CostBufferPercent"/> as it was last saved instead
    /// (RawMaterialRules.TargetBufferPercentOf), and for a made or linked ingredient.
    /// </summary>
    public decimal? TargetCostBufferPercent { get; set; }

    /// <summary>
    /// Purchased only: the brand its cost comes from (2026-10-29, at the user's request: cost by preferred brand), the
    /// dearest of that brand's prices when it's priced in more than one unit. Null for the dearest brand, as before, which
    /// is also what a brand that no longer has a price falls back to (RawMaterialRules.FindCostBrandPrice).
    /// </summary>
    public string? PreferredBrand { get; set; }

    /// <summary>
    /// The cost of one base unit with the buffer on top: what recipes, sub-assemblies, packaging lines
    /// and products are costed with, while <see cref="CostPerBaseUnit"/> stays the true cost. Stored
    /// alongside it: for Purchased it's CostPerBaseUnit plus the buffer, for Made the BOM rollup of the
    /// components' buffered costs and nothing more, so a buffer is counted once however deep the tree.
    /// </summary>
    public decimal BufferedCostPerBaseUnit { get; set; }

    /// <summary>
    /// When the material was last saved, in local time: set each time it's added or updated from a form, as
    /// Product.UpdatedAt is. A cost change on its own doesn't move it — that's <see cref="CostUpdatedAt"/>.
    /// Null for a material not saved since this was added.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// When <see cref="CostPerBaseUnit"/> or <see cref="BufferedCostPerBaseUnit"/> last changed, in local time.
    /// Stamped by the DbContext from what actually changed, so every path counts: a new brand price, a buffer or
    /// buffered-cost edit, a BOM or weight-loss edit, a unit multiplier correction, and the cascade reaching it
    /// from a component. Null for a material whose cost hasn't moved since this was added.
    /// </summary>
    public DateTime? CostUpdatedAt { get; set; }

    /// <summary>The units it's bought or used in besides its base unit.</summary>
    public List<RawMaterialUnit> Units { get; set; } = [];

    /// <summary>
    /// A copy to give other costs to without touching this one, as the Price Simulator screen prices with (2026-10-03). It shares
    /// this material's category, base unit and units, which no cost lives in. Every property comes with it, so one added
    /// later is never left behind.
    /// </summary>
    public RawMaterial ShallowCopy() => (RawMaterial)MemberwiseClone();
}
