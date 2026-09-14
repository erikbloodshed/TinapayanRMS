using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// Keeps every made ingredient's cost per gram (CostPerBaseUnit) correct after something it
/// (directly or transitively) depends on changes cost, then hands off to SubAssemblyCostPropagator so
/// every sub-assembly using any of the changed ingredients stays correct too (Phase 11 —
/// the sub-assembly branch of the reactive costing cascade). An Infrastructure-internal
/// collaborator, not exposed as an ApplicationCore interface — callers are other
/// repositories, never the UI.
/// </summary>
public class IngredientCostPropagator(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory, SubAssemblyCostPropagator subAssemblyCostPropagator)
{
    /// <summary>
    /// Call after anything that can change changedIngredientId's own cost: a price
    /// history add/update/delete (Phase 5), or a BOM line / weight loss percent edit
    /// (this phase). If changedIngredientId is itself Made, its own cost is
    /// recomputed first from its current BOM lines; then every made ingredient that
    /// transitively depends on it is recomputed too, in dependency order; then every
    /// sub-assembly that uses any of those ingredients — or changedIngredientId itself,
    /// whose own cost the caller already updated before calling this — is recomputed.
    /// Only ingredients take part: packaging is never in a BOM or a sub-assembly.
    /// </summary>
    public async Task RecomputeAndPropagateAsync(int changedIngredientId, CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var ingredients = await dbContext.RawMaterials
            .Where(m => m.Type == RawMaterialType.Ingredient)
            .ToDictionaryAsync(m => m.Id, cancellationToken);
        var bomLines = await dbContext.IngredientBomLines.ToListAsync(cancellationToken);
        var linesByIngredientId = bomLines.ToLookup(l => l.IngredientId);

        var componentIdsByIngredientId = bomLines
            .GroupBy(l => l.IngredientId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(l => l.ComponentIngredientId).ToList());

        var order = new List<int>();
        if (ingredients.TryGetValue(changedIngredientId, out var changed) && changed.Source == RawMaterialSource.Made)
        {
            order.Add(changedIngredientId);
        }

        order.AddRange(IngredientCostGraph.GetDependentsInOrder(changedIngredientId, componentIdsByIngredientId));

        if (order.Count > 0)
        {
            // Two rollups over the same lines: the true cost from the components' true costs, and the
            // buffered cost from their buffered costs with this ingredient's own buffer on top.
            foreach (var id in order)
            {
                var ingredient = ingredients[id];
                var lines = linesByIngredientId[id].ToList();
                var weightLossPercent = ingredient.WeightLossPercent ?? 0m;

                var (_, costPerGram) = IngredientBomRules.CalculateCost(
                    [.. lines.Select(l => (l.Grams, ComponentCostPerGram: ingredients[l.ComponentIngredientId].CostPerBaseUnit))],
                    weightLossPercent);
                var (_, componentsBufferedCostPerGram) = IngredientBomRules.CalculateCost(
                    [.. lines.Select(l => (l.Grams, ComponentCostPerGram: ingredients[l.ComponentIngredientId].BufferedCostPerBaseUnit))],
                    weightLossPercent);

                ingredient.CostPerBaseUnit = costPerGram;
                ingredient.BufferedCostPerBaseUnit = RawMaterialRules.BufferedCost(componentsBufferedCostPerGram, ingredient.CostBufferPercent);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Every ingredient whose cost just changed: changedIngredientId itself (its
        // own cost may already have been updated by the caller before this method ran —
        // e.g. a new price history entry) plus every made ingredient just recomputed above.
        var allChangedIngredientIds = new HashSet<int>(order) { changedIngredientId };
        await subAssemblyCostPropagator.RecomputeAndPropagateAsync(allChangedIngredientIds, cancellationToken);
    }
}
