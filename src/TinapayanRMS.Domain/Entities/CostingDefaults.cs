namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// The labor, overhead, administrative, markup and VAT percentages a new product's costing starts with, and the
/// shrinkage a new Recipe Structure section starts with, set in Settings. One row at most (Id 1); none until they're
/// first saved, which reads as the costing at zero and the shrinkage at <see cref="DefaultShrinkagePercent"/>. A product
/// keeps its own CostingParameters, and a section its own shrinkage: changing these never touches a saved product.
/// </summary>
public class CostingDefaults
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    /// <summary>The shrinkage a new section starts with until Settings says otherwise.</summary>
    public const decimal DefaultShrinkagePercent = 3m;

    public int Id { get; set; } = SingletonId;

    public decimal LaborPercent { get; set; }

    public decimal OverheadPercent { get; set; }

    public decimal AdministrativePercent { get; set; }

    /// <summary>Markup on cost, as CostingParameters.MarginPercent.</summary>
    public decimal MarginPercent { get; set; }

    public decimal VatPercent { get; set; }

    /// <summary>A new Recipe Structure section's Shrinkage %, as ProductSubAssemblyUsage.ShrinkagePercent: 0 to under 100.</summary>
    public decimal ShrinkagePercent { get; set; } = DefaultShrinkagePercent;
}
