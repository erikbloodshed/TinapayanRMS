using Microsoft.EntityFrameworkCore;

namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>
/// Ticks Into other sections (SubAssemblyRole.CanGoIntoOtherSections) on every role that already has a section on
/// another's lines (UsageSectionLine) as the tick comes in, so no saved product's next Save is refused for a line it had,
/// and the picker offers what was used. Every other role starts unticked.
/// </summary>
internal static class RolesIntoOtherSectionsUpgrade
{
    public static async Task RunAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.SubAssemblyRoles
            .Where(r => !r.IsPackaging
                && dbContext.UsageSectionLines.Any(l => dbContext.SubAssemblies.Any(s => s.Id == l.SectionSubAssemblyId && s.RoleId == r.Id)))
            .ExecuteUpdateAsync(set => set.SetProperty(r => r.CanGoIntoOtherSections, true), cancellationToken);
}
