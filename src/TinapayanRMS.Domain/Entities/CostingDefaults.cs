namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// The labor, overhead, administrative, markup and VAT percentages a new product's costing starts with, set in
/// Settings. One row at most (Id 1); none until they're first saved, which reads as all zero. A product keeps its own
/// CostingParameters from then on: changing these never touches a saved product.
/// </summary>
public class CostingDefaults
{
    /// <summary>The one row's id.</summary>
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public decimal LaborPercent { get; set; }

    public decimal OverheadPercent { get; set; }

    public decimal AdministrativePercent { get; set; }

    /// <summary>Markup on cost, as CostingParameters.MarginPercent.</summary>
    public decimal MarginPercent { get; set; }

    public decimal VatPercent { get; set; }
}
