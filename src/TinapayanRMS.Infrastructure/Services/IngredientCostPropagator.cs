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
            var dependencyIdsByIngredientId = bomLines
                .Select(l => (l.IngredientId, DependencyId: l.ComponentIngredientId))
                .Concat(ingredients.Values
                    .Where(m => m is { Source: RawMaterialSource.Linked, LinkedToId: not null })
                    .Select(m => (IngredientId: m.Id, DependencyId: m.LinkedToId!.Value)))
                .GroupBy(e => e.IngredientId)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)g.Select(e => e.DependencyId).ToList());

            return new IngredientGraph(ingredients, bomLines.ToLookup(l => l.IngredientId), dependencyIdsByIngredientId);
        }

        /// <summary>
        /// Recomputes one made or linked ingredient from its components' or linked-to ingredient's costs, which must already
        /// be correct (dependency order), reading each directly off the loaded ingredients rather than re-walking its own
        /// tree. Returns whether anything stored changed. Each cost is stored to the column's 20 places (RawMaterialRules.StoredCost),
        /// so a cost recomputed from unchanged inputs equals the stored one and isn't saved, or dated, again.
        /// </summary>
        public bool Recompute(int id)
        {
            var ingredient = ingredients[id];
            var (costBefore, bufferedBefore, bufferBefore) = (ingredient.CostPerBaseUnit, ingredient.BufferedCostPerBaseUnit, ingredient.CostBufferPercent);

            // The same share of the linked-to ingredient's true cost and of its buffered cost, so a part follows the
            // linked-to ingredient's fixed buffered cost. The buffer percent is copied for show, so a buffer change on the
            // linked-to ingredient (which cascades) reaches its parts, down a chain of links.
            if (ingredient is { Source: RawMaterialSource.Linked, LinkedToId: { } linkedToId, WeightSharePercent: { } weightShare, CostSharePercent: { } costShare })
            {
                var linkedTo = ingredients[linkedToId];
                ingredient.CostBufferPercent = linkedTo.CostBufferPercent;
                ingredient.CostPerBaseUnit = RawMaterialRules.StoredCost(RawMaterialRules.LinkedCost(linkedTo.CostPerBaseUnit, weightShare, costShare));
                ingredient.BufferedCostPerBaseUnit = RawMaterialRules.StoredCost(RawMaterialRules.LinkedCost(linkedTo.BufferedCostPerBaseUnit, weightShare, costShare));
            }
            else
            {
                // Two rollups over the same lines: the true cost from the components' true costs, and the buffered cost
                // from their buffered costs — with no buffer of its own on top (2026-09-21, at the user's request), since
                // every component's own buffer is already inside the figures being rolled up. The weight loss still
                // divides both. Its stored percent is normalised to 0 so nothing reads it as meaningful: a made ingredient
                // has no buffer box on the screen.
                var lines = linesByIngredientId[id].ToList();
                var weightLossPercent = ingredient.WeightLossPercent ?? 0m;

                var (_, costPerGram) = IngredientBomRules.CalculateCost(
                    [.. lines.Select(l => (l.Grams, ComponentCostPerGram: ingredients[l.ComponentIngredientId].CostPerBaseUnit))],
                    weightLossPercent);
                var (_, componentsBufferedCostPerGram) = IngredientBomRules.CalculateCost(
                    [.. lines.Select(l => (l.Grams, ComponentCostPerGram: ingredients[l.ComponentIngredientId].BufferedCostPerBaseUnit))],
                    weightLossPercent);

                ingredient.CostPerBaseUnit = RawMaterialRules.StoredCost(costPerGram);
                ingredient.BufferedCostPerBaseUnit = RawMaterialRules.StoredCost(componentsBufferedCostPerGram);
                ingredient.CostBufferPercent = 0m;
            }

            return ingredient.CostPerBaseUnit != costBefore
                || ingredient.BufferedCostPerBaseUnit != bufferedBefore
                || ingredient.CostBufferPercent != bufferBefore;
        }
    }
}
