using TinapayanRMS.Domain;

namespace TinapayanRMS.ApplicationCore;

public interface IProductSubAssemblyUsageRepository
{
    /// <summary>The product's own owned usage rows only — never resolved through a tab link. Used internally (e.g. to materialize a tab being unlinked); UI screens should use GetEffectiveForProductAsync instead.</summary>
    Task<IReadOnlyList<ProductSubAssemblyUsage>> GetForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>
    /// The usages a product actually shows for every role: a role whose tab is Live
    /// Template-linked resolves through to the linked product's tab (recursively, if that
    /// product's own tab is itself linked); every other role returns its own owned rows.
    /// </summary>
    Task<IReadOnlyList<ProductSubAssemblyUsage>> GetEffectiveForProductAsync(int productId, CancellationToken cancellationToken);

    /// <summary>The GetEffectiveForProductAsync resolution, scoped to just one role — used to materialize a single tab when it's unlinked.</summary>
    Task<IReadOnlyList<ProductSubAssemblyUsage>> GetEffectiveForRoleAsync(int productId, int subAssemblyRoleId, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces one role's (one tab's) usage set — the other roles' usages are untouched.
    /// Rejected if this role's tab is currently Live Template-linked; unlink it first.
    /// </summary>
    Task SetUsagesForRoleAsync(int productId, int subAssemblyRoleId, IReadOnlyList<(int SubAssemblyId, decimal WeightPerPiece)> usages, CancellationToken cancellationToken);
}
