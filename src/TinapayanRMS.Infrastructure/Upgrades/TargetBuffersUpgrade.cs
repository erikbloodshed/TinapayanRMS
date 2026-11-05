using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>
/// Gives every purchased material saved before targets were kept (RawMaterial.TargetCostBufferPercent) the target it was
/// read as: its buffer percent as last saved. Every save stores one, so after this every purchased material has its own.
/// </summary>
internal static class TargetBuffersUpgrade
{
    public static async Task RunAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken) =>
        await dbContext.RawMaterials
            .Where(m => m.Source == RawMaterialSource.Purchased && m.TargetCostBufferPercent == null)
            .ExecuteUpdateAsync(set => set.SetProperty(m => m.TargetCostBufferPercent, m => (decimal?)m.CostBufferPercent), cancellationToken);
}
