namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// Labor, overhead, administrative, margin and VAT for one product's selling price. Always independent
/// per product, even for a derived/linked product (Phase 10) — never live-templated.
/// </summary>
public class CostingParameters
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Labor cost, as a percentage of the sub-assembly (ingredient) cost per piece; packaging isn't in the base.</summary>
    public decimal LaborPercent { get; set; }

    /// <summary>Overhead cost, as a percentage of the sub-assembly cost per piece.</summary>
    public decimal OverheadPercent { get; set; }

    /// <summary>Administrative cost (office, permits, bookkeeping and the like), as a percentage of the sub-assembly cost per piece.</summary>
    public decimal AdministrativePercent { get; set; }

    /// <summary>Markup on cost (materials + labor + overhead + administrative), as a percentage.</summary>
    public decimal MarginPercent { get; set; }

    /// <summary>Added on top of the marked-up price, as a percentage.</summary>
    public decimal VatPercent { get; set; }
}
