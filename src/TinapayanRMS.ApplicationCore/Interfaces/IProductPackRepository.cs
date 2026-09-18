using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// One sellable pack as Save writes it, with its own packaging lines — its box, label and ribbon, never
/// the wrapper around each piece. Id is 0 for a new one, which is why the lines travel in here rather
/// than in a flat list beside the per-piece ones: a pack being added has no id for a line to name yet.
/// AuthoredValue is whichever of QtyPerUnit/UnitsPerPackagingUnit the user typed for that line, and for
/// a pack's line "unit" is one pack.
/// </summary>
public sealed record ProductPackDraft(
    int Id,
    string Name,
    int PiecesPerPack,
    IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> Lines);

public interface IProductPackRepository
{
    /// <summary>
    /// The product's packs, smallest first, each carrying its own packaging lines with their materials,
    /// base units and entry units loaded.
    /// </summary>
    Task<IReadOnlyList<ProductPack>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the product's whole set of packs and each pack's lines: a pack with an Id is updated and
    /// keeps it, one without is added, and one no longer listed is deleted along with its lines. Rejects a
    /// nameless pack, one holding no pieces, and two packs with the same name (ProductRules.EnsureValidPacks).
    /// Never resolved through a packaging link — a pack is always its own product's.
    /// </summary>
    Task SetPacksAsync(int productId, IReadOnlyList<ProductPackDraft> packs, CancellationToken cancellationToken);
}
