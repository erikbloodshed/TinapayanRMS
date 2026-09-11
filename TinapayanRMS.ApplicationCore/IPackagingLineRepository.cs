using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IPackagingLineRepository
{
    Task<IReadOnlyList<PackagingLine>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the product's whole packaging line set. AuthoredValue is whichever of
    /// QtyPerPiece/PiecesPerPackagingUnit the user actually typed for that line (per
    /// AuthoredField) — the repository derives the other one (ProductRules.ReconcileReciprocal).
    /// </summary>
    Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int EntryUnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken);
}
