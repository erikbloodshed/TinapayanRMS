using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Queries;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// Keeps every made and linked ingredient's costs (CostPerBaseUnit, BufferedCostPerBaseUnit) correct after something it
/// (directly or transitively) depends on changes cost: the stored part of the reactive costing cascade, and all of it, since
/// sub-assemblies and products store no cost and are costed from their lines as they're read. An Infrastructure-internal
/// collaborator, not exposed as an ApplicationCore interface — callers are other repositories and CostRecalculator, never
/// the UI.
///
/// It runs on the caller's context, inside the caller's transaction, so a save that changes a cost and the cascade it
/// causes commit together or not at all: a cascade that failed part-way after its save had committed would leave the
/// stored costs above it stale, and nothing would put them right, since saving again unchanged doesn't cascade.
/// </summary>
internal static class IngredientCostPropagator
{
    /// <summary>
    /// Call after anything that can change changedIngredientId's own cost has been saved on <paramref name="dbContext"/>,
    /// before its transaction commits: a brand price, a made ingredient's sub-assembly, add-in or weight loss percent edit,
    /// its sub-assembly's lines changing (SubAssemblyLineRepository.SetLinesAsync), a link or share edit, a buffer edit, or
    /// a corrected unit multiplier. If changedIngredientId is itself Made or Linked, its own cost is recomputed first from
    /// its current BOM (IngredientBoms: its sub-assembly's lines and its add-ins) or link; then every made or linked
    /// ingredient that transitively depends on it is recomputed too, in dependency order. Only ingredients take part:
    /// packaging is never in a BOM, a link or a sub-assembly.
    /// </summary>
    public static Task RecomputeAndPropagateAsync(TinapayanRMSDbContext dbContext, int changedIngredientId, CancellationToken cancellationToken) =>
        RecomputeAndPropagateAsync(dbContext, [changedIngredientId], cancellationToken);

    /// <summary>
    /// The same for several ingredients one save changed (Re-apply Target Buffers, a sub-assembly several made ingredients
    /// are made from): the graph is loaded once, and every ingredient they reach is recomputed once, in dependency order,
    /// however many of them changed.
    /// </summary>
    public static async Task RecomputeAndPropagateAsync(
        TinapayanRMSDbContext dbContext, IReadOnlyCollection<int> changedIngredientIds, CancellationToken cancellationToken)
    {
        if (changedIngredientIds.Count == 0)
        {
            return;
        }

        var graph = await IngredientGraph.LoadAsync(dbContext, cancellationToken);

        // The changed ones that are made or linked, whose own cost is worked out here, and every one depending on any of them.
        HashSet<int> affected =
        [
            .. changedIngredientIds.Where(id => graph.Ingredients.TryGetValue(id, out var changed) && changed.Source != RawMaterialSource.Purchased),
        ];
        affected.UnionWith(IngredientCostGraph.GetDependents(changedIngredientIds, IngredientCostGraph.DependentsOf(graph.DependencyIdsByIngredientId)));

        if (affected.Count == 0)
        {
            return;
        }

        foreach (var id in IngredientCostGraph.InDependencyOrder(affected, graph.DependencyIdsByIngredientId))
        {
            graph.Recompute(id);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Recomputes every made and linked ingredient, in dependency order, from what it's made of or linked to as stored, and
    /// saves on <paramref name="dbContext"/>. Returns how many had a stored cost that was wrong.
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

    /// <summary>
    /// Every ingredient, tracked, with every BOM line, and what each depends on: its BOM components, or the one it's linked
    /// to. A made ingredient's BOM is its sub-assembly's lines and its add-ins, so a sub-assembly is never a node: its lines'
    /// ingredients are the made ingredient's components, and ordering them needs nothing new.
    /// </summary>
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
            var bomLines = await IngredientBoms.ForAsync(dbContext, null, cancellationToken);

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
