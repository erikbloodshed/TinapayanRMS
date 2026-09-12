namespace TinapayanRMS.Domain;

/// <summary>Which of PackagingLine's two reciprocal fields the user actually typed; the other is recalculated from it.</summary>
public enum PackagingLineAuthoredField
{
    QtyPerPiece,
    PiecesPerPackagingUnit,
}
