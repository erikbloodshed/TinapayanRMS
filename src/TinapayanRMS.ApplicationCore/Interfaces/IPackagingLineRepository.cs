using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IPackagingLineRepository
{
    /// <summary>The product's own owned lines only — never resolved through a tab link. Used internally (e.g. to materialize the Packaging tab being unlinked); UI screens should use GetEffectiveForProductAsync instead.</summary>
    Task<IReadOnlyList<PackagingLine>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// The packaging lines a product actually shows: if its one Packaging tab is Live
    /// Template-linked, resolves through to the linked product's tab (recursively, if that
    /// product's own Packaging tab is itself linked); otherwise returns its own owned lines.
    /// </summary>
    Task<IReadOnlyList<PackagingLine>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the product's whole packaging line set. AuthoredValue is whichever of
    /// QtyPerPiece/PiecesPerPackagingUnit the user actually typed for that line (per
    /// AuthoredField) — the repository derives the other one (ProductRules.ReconcileReciprocal).
    /// Each line's quantities are in UnitId, one of that material's own units, or in its base unit
    /// when that's null. Every line must be a packaging material (RawMaterialRules.EnsureUsableAsPackaging).
    /// Rejected if the Packaging tab is currently Live Template-linked; unlink it first.
    /// </summary>
    Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken);
}
