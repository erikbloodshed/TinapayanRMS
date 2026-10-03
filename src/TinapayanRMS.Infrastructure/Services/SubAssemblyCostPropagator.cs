using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// Keeps every SubAssembly's TotalWeight/TotalCost correct after one or more ingredients it uses change cost, on the
/// caller's context and inside its transaction, as IngredientCostPropagator runs. An Infrastructure-internal
/// collaborator, not exposed as an ApplicationCore interface — the callers are IngredientCostPropagator and
/// CostRecalculator, never the UI.
///
/// SubAssemblyLine only ever references Ingredient, never another SubAssembly (Phase 7), so — unlike the ingredient BOM
/// tree — there's no nesting and no dependency order to work out here: every affected sub-assembly is recomputed directly
/// from its own lines in one pass.
/// </summary>
internal static class SubAssemblyCostPropagator
{
    /// <summary>
    /// Call after one or more ingredients' costs have changed and been saved on <paramref name="dbContext"/>. A
    /// sub-assembly is costed with its ingredients' buffered costs (BufferedCostPerBaseUnit). Recomputes every
    /// sub-assembly that has a line referencing any of them, from all of its lines, and saves; a no-op if none do.
    /// </summary>
    public static async Task RecomputeAsync(TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> changedIngredientIds, CancellationToken cancellationToken)
    {
        if (changedIngredientIds.Count == 0)
        {
            return;
        }

        var affectedSubAssemblyIds = await dbContext.SubAssemblyLines
            .Where(l => changedIngredientIds.Contains(l.IngredientId))
            .Select(l => l.SubAssemblyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (affectedSubAssemblyIds.Count > 0)
        {
            await RecomputeAsync(dbContext, dbContext.SubAssemblies.Where(s => affectedSubAssemblyIds.Contains(s.Id)), cancellationToken);
        }
    }

    /// <summary>
    /// Recomputes every sub-assembly from its lines and saves on <paramref name="dbContext"/>, after every ingredient's
    /// cost is correct (CostRecalculator). Returns how many had stored totals that were wrong. A packaging set has no
    /// ingredient lines, and its totals are 0.
    /// </summary>
    public static Task<int> RecomputeAllAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken) =>
        RecomputeAsync(dbContext, dbContext.SubAssemblies, cancellationToken);

    /// <summary>
    /// Recomputes <paramref name="query"/>'s sub-assemblies from all of their lines' current ingredient costs, in one
    /// loaded-once-saved-once pass, and returns how many changed. Totals are stored to the column's 4 places, so totals
    /// recomputed from unchanged costs equal the stored ones and aren't written again.
    /// </summary>
    private static async Task<int> RecomputeAsync(TinapayanRMSDbContext dbContext, IQueryable<SubAssembly> query, CancellationToken cancellationToken)
    {
        var subAssemblies = await query.ToListAsync(cancellationToken);
        var subAssemblyIds = subAssemblies.Select(s => s.Id).ToList();
        var linesBySubAssemblyId = (await dbContext.SubAssemblyLines
                .Where(l => subAssemblyIds.Contains(l.SubAssemblyId))
                .Include(l => l.Ingredient)
                .ToListAsync(cancellationToken))
            .ToLookup(l => l.SubAssemblyId);

        var changed = 0;
        foreach (var subAssembly in subAssemblies)
        {
            var costInputs = linesBySubAssemblyId[subAssembly.Id]
                .Select(l => (l.Grams, IngredientCostPerGram: l.Ingredient!.BufferedCostPerBaseUnit))
                .ToList();

            var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(costInputs);
            totalWeight = TinapayanRMSDbContext.Stored(totalWeight);
            totalCost = TinapayanRMSDbContext.Stored(totalCost);
            if (subAssembly.TotalWeight != totalWeight || subAssembly.TotalCost != totalCost)
            {
                changed++;
            }

            subAssembly.TotalWeight = totalWeight;
            subAssembly.TotalCost = totalCost;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return changed;
    }
}
