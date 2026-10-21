using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// Keeps every made and linked ingredient's costs (CostPerBaseUnit, BufferedCostPerBaseUnit) correct after something it
/// (directly or transitively) depends on changes cost, then hands off to SubAssemblyCostPropagator so every sub-assembly
/// using any of the changed ingredients stays correct too (Phase 11 — the sub-assembly branch of the reactive costing
/// cascade). An Infrastructure-internal collaborator, not exposed as an ApplicationCore interface — callers are other
/// repositories and CostRecalculator, never the UI.
///
/// It runs on the caller's context, inside the caller's transaction (2026-10-03), so a save that changes a cost and the
/// cascade it causes commit together or not at all. Until then it ran after the caller's commit, on contexts of its own:
/// a cascade that failed part-way (a dropped connection, the app closed) left the save in and the stored costs above it
/// stale, and nothing ever put them right, since saving again unchanged doesn't cascade.
/// </summary>
internal static class IngredientCostPropagator
{
    /// <summary>
    /// Call after anything that can change changedIngredientId's own cost has been saved on <paramref name="dbContext"/>,
    /// before its transaction commits: a brand price, a BOM line or weight loss percent edit, a link or share edit, a
    /// buffer edit, or a corrected unit multiplier. If changedIngredientId is itself Made or Linked, its own cost is
    /// recomputed first from its current BOM lines or link; then every made or linked ingredient that transitively depends
    /// on it is recomputed too, in dependency order; then every sub-assembly that uses any of those ingredients — or
    /// changedIngredientId itself, whose own cost the caller already updated — is recomputed. Only ingredients take part:
    /// packaging is never in a BOM, a link or a sub-assembly.
    /// </summary>
    public static async Task RecomputeAndPropagateAsync(TinapayanRMSDbContext dbContext, int changedIngredientId, CancellationToken cancellationToken)
    {
        var graph = await IngredientGraph.LoadAsync(dbContext, cancellationToken);

        var order = new List<int>();
        if (graph.Ingredients.TryGetValue(changedIngredientId, out var changed) && changed.Source != RawMaterialSource.Purchased)
        {
            order.Add(changedIngredientId);
        }

        order.AddRange(IngredientCostGraph.GetDependentsInOrder(changedIngredientId, graph.DependencyIdsByIngredientId));

        if (order.Count > 0)
        {
            foreach (var id in order)
            {
                graph.Recompute(id);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
        }

        // Every ingredient whose cost just changed: changedIngredientId itself (its own cost may already have been updated
        // by the caller before this method ran — e.g. a new brand price) plus every made ingredient just recomputed above.
        var allChangedIngredientIds = new HashSet<int>(order) { changedIngredientId };
        await SubAssemblyCostPropagator.RecomputeAsync(dbContext, allChangedIngredientIds, cancellationToken);
    }

    /// <summary>
    /// Recomputes every made and linked ingredient, in dependency order, from what it's made of or linked to as stored, and
    /// saves on <paramref name="dbContext"/>. Returns how many had a stored cost that was wrong. The sub-assemblies are the
    /// caller's to recompute after (CostRecalculator).
    /// </summary>
    public static async Task<int> RecomputeAllAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken)
    {
        var graph = await IngredientGraph.LoadAsync(dbContext, cancellationToken);
        var derivedIds = graph.Ingredients.Values.Where(m => m.Source != RawMaterialSource.Purchased).Select(m => m.Id).ToHashSet();

        var corrected = 0;
        foreach (var id in IngredientCostGraph.InDependencyOrder(derivedIds, graph.DependencyIdsByIngredientId))
        {
            if (graph.Recompute(id))
            {
                corrected++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return corrected;
    }

    /// <summary>Every ingredient, tracked, with every BOM line, and what each depends on: its BOM components, or the one it's linked to.</summary>
    private sealed class IngredientGraph(
        Dictionary<int, RawMaterial> ingredients,
        ILookup<int, IngredientBomLine> linesByIngredientId,
        Dictionary<int, IReadOnlyList<int>> dependencyIdsByIngredientId)
    {
        public IReadOnlyDictionary<int, RawMaterial> Ingredients => ingredients;

        public IReadOnlyDictionary<int, IReadOnlyList<int>> DependencyIdsByIngredientId => dependencyIdsByIngredientId;

        public static async Task<IngredientGraph> LoadAsync(TinapayanRMSDbContext dbContext, CancellationToken cancellationToken)
        {
            var ingredients = await dbContext.RawMaterials
                .Where(m => m.Type == RawMaterialType.Ingredient)
                .ToDictionaryAsync(m => m.Id, cancellationToken);
            var bomLines = await dbContext.IngredientBomLines.ToListAsync(cancellationToken);

            // BOM components and links, from the ingredients and lines already loaded.
            var dependencyIdsByIngredientId = IngredientCostRules.Dependencies(ingredients.Values, bomLines);

            return new IngredientGraph(ingredients, bomLines.ToLookup(l => l.IngredientId), dependencyIdsByIngredientId);
        }

        /// <summary>
        /// Recomputes one made or linked ingredient from its components' or linked-to ingredient's costs, which must already
        /// be correct (dependency order), reading each directly off the loaded ingredients rather than re-walking its own
        /// tree (IngredientCostRules.Recompute, which the Price Simulator screen shares). Returns whether anything stored changed:
        /// a cost recomputed from unchanged inputs equals the stored one, so it isn't saved, or dated, again.
        /// </summary>
        public bool Recompute(int id)
        {
            var ingredient = ingredients[id];
            var before = new IngredientCosts(ingredient.CostPerBaseUnit, ingredient.BufferedCostPerBaseUnit, ingredient.CostBufferPercent);
            var after = IngredientCostRules.Recompute(ingredient, linesByIngredientId[id], dependencyId => ingredients[dependencyId]);
            (ingredient.CostPerBaseUnit, ingredient.BufferedCostPerBaseUnit, ingredient.CostBufferPercent) = after;
            return after != before;
        }
    }
}
