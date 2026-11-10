using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>
/// One packaging material (a raw material of the Packaging type) used by a product, for one whole sellable
/// unit of it — the wrapper, the box, the label, the ribbon. "Unit" in the two quantity fields below means
/// that sellable unit, which is one finished piece unless the product's QuantityRequired says otherwise.
/// </summary>
public sealed class PackagingLine
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int PackagingMaterialId { get; set; }

    public RawMaterial? PackagingMaterial { get; set; }

    /// <summary>The unit this line's quantities are entered in: one of the material's own units, or null for its base unit.</summary>
    public int? UnitId { get; set; }

    public RawMaterialUnit? Unit { get; set; }

    /// <summary>Amount of the packaging material, in the line's unit, used by one sellable unit of the product.</summary>
    public decimal QtyPerUnit { get; set; }

    /// <summary>Sellable units served by one of the line's unit of the packaging material, exactly 1 / QtyPerUnit.</summary>
    public decimal UnitsPerPackagingUnit { get; set; }

    /// <summary>Which of the two fields above the user typed; the other is recalculated from it on every edit.</summary>
    public PackagingLineAuthoredField AuthoredField { get; set; }

    /// <summary>
    /// Where the line sits in its product's packaging's table, from 0: the user orders the lines by dragging them, and every save
    /// writes each one's position. Lines are read in this order, then by Id.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// A line of the product's bulk packaging: what one bulk pack takes (a box several units share, the tape that closes
    /// it), shared out among the sellable units it holds (Product.BulkPackagingQuantity). Its <see cref="QtyPerUnit"/> is
    /// per bulk pack, not per unit. Always the product's own, whether its packaging is its own lines or linked, and never
    /// read through a link (IPackagingLineRepository).
    /// </summary>
    public bool IsBulk { get; set; }
}
