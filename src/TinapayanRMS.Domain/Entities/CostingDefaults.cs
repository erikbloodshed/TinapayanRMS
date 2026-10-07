using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// The stage a new product is filed in, the labor, overhead, administrative, markup and VAT percentages its costing
/// starts with, the shrinkage a new Recipe Structure section starts with, and the cost buffer a new raw material starts
/// with, set in Settings. One row at most (Id 1); none until they're first saved, which reads as new products in
/// <see cref="DefaultNewProductStage"/>, the costing at zero, the shrinkage at <see cref="DefaultShrinkagePercent"/> and
/// the buffer at <see cref="DefaultCostBufferPercent"/>. A product keeps its own stage and CostingParameters, a section
/// its own shrinkage and a material its own buffer: changing these never touches a saved one.
/// </summary>
public class CostingDefaults
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    /// <summary>The shrinkage a new section starts with until Settings says otherwise.</summary>
    public const decimal DefaultShrinkagePercent = 3m;

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

    /// <summary>A new Recipe Structure section's Shrinkage %, as ProductSubAssemblyUsage.ShrinkagePercent: 0 to under 100.</summary>
    public decimal ShrinkagePercent { get; set; } = DefaultShrinkagePercent;

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
}
