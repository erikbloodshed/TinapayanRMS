using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>
/// Gives every product's own sub-assembly the section name it went by until section names were stored
/// (SubAssembly.SectionName, 2026-11-05), read from its name this once (SubAssemblyRules.SectionNameFromSavedName).
/// </summary>
internal static class SectionNamesUpgrade
{
    public static async Task RunAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken)
    {
        var owned = await dbContext.SubAssemblies
            .Include(s => s.OwnerProduct)
            .Include(s => s.Role)
            .Where(s => s.OwnerProductId != null)
            .ToListAsync(cancellationToken);
        foreach (var subAssembly in owned)
        {
            subAssembly.SectionName = SubAssemblyRules.SectionNameFromSavedName(subAssembly.Name, subAssembly.OwnerProduct!.Name, subAssembly.Role!.Name);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
