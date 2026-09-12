using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// Keeps every SubAssembly's TotalWeight/TotalCost correct after one or more ingredients
/// it uses change cost. An Infrastructure-internal collaborator, not exposed as an
/// ApplicationCore interface — the only caller is IngredientCostPropagator, never the UI.
///
/// SubAssemblyLine only ever references Ingredient, never another SubAssembly (Phase 7),
/// so — unlike the ingredient BOM tree — there's no nesting and no dependency order to
/// work out here: every affected sub-assembly is recomputed directly from its own lines
/// in one pass.
/// </summary>
public class SubAssemblyCostPropagator(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory)
{
    /// <summary>
    /// Call after one or more ingredients' CostPerGram values have changed and been saved.
    /// Recomputes every sub-assembly that has a line referencing any of them; a no-op if
    /// none do.
    /// </summary>
    public async Task RecomputeAndPropagateAsync(IReadOnlyCollection<int> changedIngredientIds, CancellationToken cancellationToken)
    {
        if (changedIngredientIds.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var affectedSubAssemblyIds = await dbContext.SubAssemblyLines
            .Where(l => changedIngredientIds.Contains(l.IngredientId))
            .Select(l => l.SubAssemblyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        if (affectedSubAssemblyIds.Count == 0)
        {
            return;
        }

        var subAssemblies = await dbContext.SubAssemblies
            .Where(s => affectedSubAssemblyIds.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, cancellationToken);

        var allLines = await dbContext.SubAssemblyLines
            .Where(l => affectedSubAssemblyIds.Contains(l.SubAssemblyId))
            .Include(l => l.Ingredient)
            .ToListAsync(cancellationToken);

        var linesBySubAssemblyId = allLines.ToLookup(l => l.SubAssemblyId);

        foreach (var subAssemblyId in affectedSubAssemblyIds)
        {
            var costInputs = linesBySubAssemblyId[subAssemblyId]
                .Select(l => (l.Grams, IngredientCostPerGram: l.Ingredient!.CostPerGram))
                .ToList();

            var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(costInputs);
            var subAssembly = subAssemblies[subAssemblyId];
            subAssembly.TotalWeight = totalWeight;
            subAssembly.TotalCost = totalCost;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
