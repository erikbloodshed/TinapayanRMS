using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// Anything bought or made to produce a product: an ingredient for recipes, or a packaging material
/// for a product's packaging lines (<see cref="Type"/>). Both kinds share one table, one category
/// list, alternative units and price history; the type decides where a material can be used and
/// what its base unit is (TinapayanRMS.ApplicationCore.RawMaterialRules).
/// </summary>
public class RawMaterial
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
    /// most 100. Defaults to 0, which makes the buffered cost the same as the true cost.
    /// </summary>
    public decimal CostBufferPercent { get; set; }

    /// <summary>
    /// The cost of one base unit with the buffer on top: what recipes, sub-assemblies, packaging lines
    /// and products are costed with, while <see cref="CostPerBaseUnit"/> stays the true cost. Stored
    /// alongside it: for Purchased it's CostPerBaseUnit plus the buffer, for Made the BOM rollup of the
    /// components' buffered costs plus its own buffer, so buffers stack up the BOM tree.
    /// </summary>
    public decimal BufferedCostPerBaseUnit { get; set; }

    /// <summary>The units it's bought or used in besides its base unit.</summary>
    public List<RawMaterialUnit> Units { get; set; } = [];
}
