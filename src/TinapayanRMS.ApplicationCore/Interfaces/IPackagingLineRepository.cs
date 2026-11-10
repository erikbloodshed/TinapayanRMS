using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// The packaging each of several products shows, by product id, and the ones whose packaging links come round again:
/// only an edit outside the app can leave one, since a link that would close one is refused (PackagingLinkRules).
/// </summary>
public sealed record EffectivePackaging(IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>> Lines, IReadOnlySet<int> LinkedRoundInALoop)
{
    /// <summary>
    /// Each product's bulk packaging (PackagingLine.IsBulk), by product id: its own lines for one bulk pack, never read
    /// through a link; empty for one with none.
    /// </summary>
    public IReadOnlyDictionary<int, IReadOnlyList<PackagingLine>> Bulk { get; init; } = new Dictionary<int, IReadOnlyList<PackagingLine>>();
}

public interface IPackagingLineRepository
{
    /// <summary>
    /// The product's own lines only, never resolved through a packaging link, and not its bulk packaging. UI screens
    /// should use GetEffectiveForProductAsync instead.
    /// </summary>
    Task<IReadOnlyList<PackagingLine>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// The packaging lines a product actually shows for one sellable unit: if its packaging is linked, resolves through to
    /// the linked set's or product's (recursively, if that product's packaging is itself linked); otherwise returns its own
    /// lines. Never its bulk packaging (PackagingLine.IsBulk), which GetEffectiveForProductsAsync reads beside them.
    /// </summary>
    Task<IReadOnlyList<PackagingLine>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// GetEffectiveForProductAsync for several products at once, by product id, in a few queries however many there are
    /// and however long their link chains: every link is read once and followed in memory. Products showing the same
    /// packaging share its line objects, which are only read. A product whose chain comes round again shows none, and is
    /// named in <see cref="EffectivePackaging.LinkedRoundInALoop"/>, rather than fail the read for every product.
    /// </summary>
    Task<EffectivePackaging> GetEffectiveForProductsAsync(IReadOnlyCollection<int> productIds, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the product's whole packaging line set, leaving its bulk packaging as it is. AuthoredValue is whichever of
    /// QtyPerUnit/UnitsPerPackagingUnit the user actually typed for that line (per
    /// AuthoredField) — the repository derives the other one (ProductRules.ReconcileReciprocal).
    /// Each line's quantities are in UnitId, one of that material's own units, or in its base unit
    /// when that's null. Every line must be a packaging material (RawMaterialRules.EnsureUsableAsPackaging).
    /// Rejected while the packaging is linked; unlink it first.
    /// </summary>
    Task SetLinesAsync(
        int productId,
        IReadOnlyList<(int PackagingMaterialId, int? UnitId, PackagingLineAuthoredField AuthoredField, decimal AuthoredValue)> lines,
        CancellationToken cancellationToken);
}
