using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductVersionRepository
{
    /// <summary>The product's versions, newest first, each with its snapshot (ProductVersionContent.FromJson reads it).</summary>
    Task<IReadOnlyList<ProductVersion>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>Whether the product has any version yet.</summary>
    Task<bool> HasAnyAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Records <paramref name="content"/> as the product's next version, numbered after its latest, unless it holds the same
    /// recipe and price as the latest (ProductVersionRules.IsSameRecipe): then nothing is written and null comes back.
    /// Refused when the product doesn't exist, or the note is too long.
    /// </summary>
    Task<ProductVersion?> AddAsync(
        int productId, ProductVersionContent content, DateTime savedAt, string? note, CancellationToken cancellationToken);

    /// <summary>Gives the version a new note, or takes it away (null or blank). Refused when it's too long or the version is gone.</summary>
    Task SetNoteAsync(int versionId, string? note, CancellationToken cancellationToken);
}
