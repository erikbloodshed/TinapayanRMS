using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Domain.Entities;

/// <summary>One packaging material used by a product.</summary>
public class PackagingLine
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product? Product { get; set; }

    public int PackagingMaterialId { get; set; }

    public PackagingMaterial? PackagingMaterial { get; set; }

    /// <summary>The unit this line's quantities are entered in: one of the material's own units, or null for its base unit.</summary>
    public int? PackagingMaterialUnitId { get; set; }

    public PackagingMaterialUnit? PackagingMaterialUnit { get; set; }

    /// <summary>Amount of the packaging material, in the line's unit, used by one finished piece.</summary>
    public decimal QtyPerPiece { get; set; }

    /// <summary>Finished pieces served by one of the line's unit of the packaging material — exactly 1 / QtyPerPiece.</summary>
    public decimal PiecesPerPackagingUnit { get; set; }

    /// <summary>Which of the two fields above the user typed; the other is recalculated from it on every edit.</summary>
    public PackagingLineAuthoredField AuthoredField { get; set; }
}
