using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IPackagingLinkRepository
{
    /// <summary>The product's packaging link, with the product it's linked to, or null when it owns its packaging.</summary>
    Task<PackagingLink?> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Links productId's packaging to linkedProductId's, replacing any existing link. Its own lines (if
    /// any) aren't touched — it simply reads through to linkedProductId's packaging from now on.
    /// </summary>
    Task SetLinkAsync(int productId, int linkedProductId, CancellationToken cancellationToken);

    /// <summary>
    /// Un-links the packaging: first materializes the lines it currently shows (resolved through the
    /// link) as the product's own, then removes the link. A no-op when it wasn't linked. Never leaves a
    /// product with less than it was showing a moment before.
    /// </summary>
    Task RemoveLinkAsync(int productId, CancellationToken cancellationToken);
}
