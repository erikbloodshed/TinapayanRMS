using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Interfaces;

public interface IProductTabLinkRepository
{
    Task<IReadOnlyList<ProductTabLink>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// Links productId's tab (a SubAssemblyRole tab when subAssemblyRoleId is set, otherwise
    /// the Packaging tab) to the same tab on linkedProductId, replacing any existing link for
    /// that tab. The tab's own rows (if any) aren't touched — it simply reads through to
    /// linkedProductId's tab from now on.
    /// </summary>
    Task SetLinkAsync(int productId, ProductTabKind tabKind, int? subAssemblyRoleId, int linkedProductId, CancellationToken cancellationToken);

    /// <summary>
    /// Un-links a tab: first materializes its currently-effective rows (resolved through
    /// whatever it was linked to) as the product's own owned rows, then removes the link.
    /// A no-op on the rows themselves if the tab wasn't linked. Never leaves a product with
    /// less data than it was showing a moment before.
    /// </summary>
    Task RemoveLinkAsync(int productId, ProductTabKind tabKind, int? subAssemblyRoleId, CancellationToken cancellationToken);
}
