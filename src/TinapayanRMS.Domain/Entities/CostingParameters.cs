namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// Labor, overhead, margin and VAT for one product's selling price. Always independent
/// per product, even for a derived/linked product (Phase 10) — never live-templated.
/// </summary>
public class CostingParameters
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    /// <summary>Labor cost per finished piece, in pesos.</summary>
    public decimal LaborCost { get; set; }

    /// <summary>Overhead cost per finished piece, in pesos.</summary>
    public decimal OverheadCost { get; set; }

    /// <summary>Administrative cost per finished piece, in pesos: office, permits, bookkeeping and the like.</summary>
    public decimal AdministrativeCost { get; set; }

    /// <summary>Markup on cost (materials + labor + overhead + administrative), as a percentage.</summary>
    public decimal MarginPercent { get; set; }

    /// <summary>Added on top of the marked-up price, as a percentage.</summary>
    public decimal VatPercent { get; set; }
}
