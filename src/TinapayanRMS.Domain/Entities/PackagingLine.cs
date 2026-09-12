namespace TinapayanRMS.Domain;

/// <summary>One packaging material used by a product.</summary>
public class PackagingLine
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int PackagingMaterialId { get; set; }

    public PackagingMaterial? PackagingMaterial { get; set; }

    /// <summary>
    /// The unit this line's quantities are entered in: the material's own StandardUnit,
    /// or — only when that standard unit is itself a length unit — any of the four
    /// length units (TinapayanRMS.ApplicationCore.ProductRules.EnsureValidEntryUnit).
    /// </summary>
    public int EntryUnitId { get; set; }

    public UnitOfMeasure? EntryUnit { get; set; }

    /// <summary>Amount of the packaging material, in EntryUnit, used by one finished piece.</summary>
    public decimal QtyPerPiece { get; set; }

    /// <summary>Finished pieces served by one EntryUnit of the packaging material — exactly 1 / QtyPerPiece.</summary>
    public decimal PiecesPerPackagingUnit { get; set; }

    /// <summary>Which of the two fields above the user typed; the other is recalculated from it on every edit.</summary>
    public PackagingLineAuthoredField AuthoredField { get; set; }
}
