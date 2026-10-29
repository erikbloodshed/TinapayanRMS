using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// The stage a new product is filed in, the labor, overhead, administrative, markup and VAT percentages its costing
/// starts with, the cost buffer a new raw material starts with, and the shop-wide wastage every product's costing adds,
/// set in Settings. One row at most (Id 1); none until they're first saved, which reads as new products in
/// <see cref="DefaultNewProductStage"/>, the costing at zero, the buffer at <see cref="DefaultCostBufferPercent"/> and the
/// wastage at <see cref="DefaultWastagePercent"/>. A product keeps its own stage and CostingParameters and a material its
/// own buffer: changing those defaults never touches a saved one. The wastage is different: it isn't a starting value
/// but the one figure every product is priced with, so changing it reprices them all.
/// </summary>
public class CostingDefaults
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    /// <summary>The shop-wide wastage until Settings says otherwise.</summary>
    public const decimal DefaultWastagePercent = 3m;

    /// <summary>The cost buffer a new raw material starts with until Settings says otherwise.</summary>
    public const decimal DefaultCostBufferPercent = 10m;

    /// <summary>The stage a new product goes in until Settings says otherwise.</summary>
    public const ProductStage DefaultNewProductStage = ProductStage.Production;

    public int Id { get; set; } = SingletonId;

    public decimal LaborPercent { get; set; }

    public decimal OverheadPercent { get; set; }

    public decimal AdministrativePercent { get; set; }

    /// <summary>Markup on cost, as CostingParameters.MarginPercent.</summary>
    public decimal MarginPercent { get; set; }

    public decimal VatPercent { get; set; }

    /// <summary>
    /// The shop-wide Wastage %, 0 to 100: a cost allowance for what's lost making a product, added to every product's
    /// costing as a percentage of its ingredient (sub-assembly) cost, packaging left out (CostingRules.CalculateSellingPrice).
    /// Added once, on the product's whole recipe cost, never again at each level of a composite.
    /// </summary>
    public decimal WastagePercent { get; set; } = DefaultWastagePercent;

    /// <summary>
    /// A new raw material's Cost Buffer %, as RawMaterial.CostBufferPercent: 0 to 100. Only a purchased one keeps it: a
    /// linked ingredient takes the buffer of the one it's linked to, and a made one has none of its own.
    /// </summary>
    public decimal CostBufferPercent { get; set; } = DefaultCostBufferPercent;

    /// <summary>
    /// Where the Products screen's New files a product (Production or Research and Development). New Product Here keeps
    /// its folder's stage, and a clone or variant starts in Research and Development whatever this is.
    /// </summary>
    public ProductStage NewProductStage { get; set; } = DefaultNewProductStage;

    /// <summary>
    /// What a suggested price is rounded up to a multiple of, such as 0.25 or 1 (2026-10-29): a price the shop would charge.
    /// 0 for none. Only the rounded figures shown beside the costing's exact ones follow it (CostingRules.RoundPriceUp).
    /// </summary>
    public decimal PriceRoundingStep { get; set; }
}
