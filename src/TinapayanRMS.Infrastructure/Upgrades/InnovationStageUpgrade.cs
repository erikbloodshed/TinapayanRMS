using TinapayanRMS.ApplicationCore.Rules;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Upgrades;

/// <summary>
/// Renames the stage stored as ResearchAndDevelopment to Innovation (ProductStage.Innovation), on every product and on
/// Settings' New Products In, as the stage was renamed on 2026-11-09. In SQL, as EF's own queries would look for the new
/// name: ProductStageConverter still reads the old one, for what a copy built before the rename writes after this.
/// </summary>
internal static class InnovationStageUpgrade
{
    public static async Task RunAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken)
    {
        const string former = ProductRules.FormerInnovationName;
        var innovation = nameof(ProductStage.Innovation);
        await dbContext.Database.ExecuteSqlAsync(
            $"UPDATE products SET stage = {innovation} WHERE stage = {former}", cancellationToken);
        await dbContext.Database.ExecuteSqlAsync(
            $"UPDATE costing_defaults SET new_product_stage = {innovation} WHERE new_product_stage = {former}", cancellationToken);
    }
}
