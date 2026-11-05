using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>
/// Gives every purchased material saved before targets were kept (RawMaterial.TargetCostBufferPercent, 2026-10-29) the
/// target it has been read as since: its buffer percent as last saved. Every save stores one since 2026-11-05, so after
/// this every purchased material has its own.
/// </summary>
internal static class TargetBuffersUpgrade
{
    public static async Task RunAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.RawMaterials
            .Where(m => m.Source == RawMaterialSource.Purchased && m.TargetCostBufferPercent == null)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.TargetCostBufferPercent, m => (decimal?)m.CostBufferPercent), cancellationToken);
}
