using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>A version to record (IProductVersionRepository.AddRangeAsync), as AddAsync takes one.</summary>
public sealed record ProductVersionDraft(int ProductId, ProductVersionContent Content, DateTime SavedAt, string? Note)
{
    /// <summary>How the product stood before, recorded first as its first version when it has none yet; or null.</summary>
    public ProductVersionDraft? Baseline { get; init; }
}

public interface IProductVersionRepository
{
    /// <summary>The product's versions, newest first, each with its snapshot (ProductVersionContent.FromJson reads it).</summary>
    Task<IReadOnlyList<ProductVersion>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Whether the product has any version yet.</summary>
    Task<bool> HasAnyAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Each product's latest version saved on or before <paramref name="asOf"/> (Reports' Margin Erosion), in one
    /// read; a product with none by then isn't there.
    /// </summary>
    Task<IReadOnlyList<ProductVersion>> GetLatestAsOfAsync(DateTime asOf, CancellationToken cancellationToken);

    /// <summary>
    /// Records <paramref name="content"/> as the product's next version, numbered after its latest, unless it holds the same
    /// recipe and price as the latest (ProductVersionRules.IsSameRecipe): then nothing is written and null comes back.
    /// Refused when the product doesn't exist, or the note is too long.
    /// </summary>
    Task<ProductVersion?> AddAsync(
        int productId, ProductVersionContent content, DateTime savedAt, string? note, CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="AddAsync"/> for many products at once, in one transaction: a save elsewhere that changed them all. A
    /// draft's <see cref="ProductVersionDraft.Baseline"/> goes in first when its product has no versions yet. Returns one
    /// entry a draft, in order: the version it recorded (not its baseline), or null when it held the latest's recipe and
    /// price, or its product is gone. Refused, with nothing written, when a note is too long.
    /// </summary>
    Task<IReadOnlyList<ProductVersion?>> AddRangeAsync(IReadOnlyList<ProductVersionDraft> drafts, CancellationToken cancellationToken);

    /// <summary>Gives the version a new note, or takes it away (null or blank). Refused when it's too long or the version is gone.</summary>
    Task SetNoteAsync(int versionId, string? note, CancellationToken cancellationToken);
}
