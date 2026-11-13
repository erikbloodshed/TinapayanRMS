using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>A made or linked ingredient's three stored cost figures, as the cascade works them out.</summary>
public readonly record struct IngredientCosts(decimal CostPerBaseUnit, decimal BufferedCostPerBaseUnit, decimal CostBufferPercent);

/// <summary>
/// How a made or linked ingredient's costs follow from what it's made of or cut from. The cascade stores what this works out; the Price Simulator screen works it out over prices that
/// aren't real (<see cref="Reprice"/>). One piece of code for both, so a simulation can never price an ingredient differently
/// from how a real price change would.
/// </summary>
public static class IngredientCostRules
{
    /// <summary>
    /// <paramref name="ingredient"/>'s costs from those of what it depends on, which must already be right (dependency
    /// order), found by <paramref name="find"/>. Each cost is at the stored column's 20 places (RawMaterialRules.StoredCost),
    /// so a cost worked out again from unchanged inputs equals the stored one.
    /// <list type="bullet">
    /// <item>Linked: the same share of the linked-to ingredient's true cost and of its buffered cost, so a part follows the
    /// linked-to one's fixed buffered cost, and its buffer percent copied for show, so a buffer change reaches the parts.</item>
    /// <item>Made (or a link missing a field): two rollups of its BOM lines, the true cost from the components' true costs
    /// and the buffered cost from their buffered ones, with no buffer of its own on top, since every component's buffer is
    /// already in its figure. The weight loss divides both. Its buffer
    /// percent is 0, as a made ingredient has no buffer box on the screen.</item>
    /// </list>
    /// </summary>
    public static IngredientCosts Recompute(RawMaterial ingredient, IEnumerable<IngredientBomLine> bomLines, Func<int, RawMaterial> find)
    {
        if (ingredient is { Source: RawMaterialSource.Linked, LinkedToId: { } linkedToId, WeightSharePercent: { } weightShare, CostSharePercent: { } costShare })
        {
            var linkedTo = find(linkedToId);
            return new IngredientCosts(
                RawMaterialRules.StoredCost(RawMaterialRules.LinkedCost(linkedTo.CostPerBaseUnit, weightShare, costShare)),
                RawMaterialRules.StoredCost(RawMaterialRules.LinkedCost(linkedTo.BufferedCostPerBaseUnit, weightShare, costShare)),
                linkedTo.CostBufferPercent);
        }

        var lines = bomLines.ToList();
        var weightLossPercent = ingredient.WeightLossPercent ?? 0m;
        var (_, costPerGram) = IngredientBomRules.CalculateCost(
            [.. lines.Select(l => (l.Grams, ComponentCostPerGram: find(l.ComponentIngredientId).CostPerBaseUnit))],
            weightLossPercent);
        var (_, bufferedCostPerGram) = IngredientBomRules.CalculateCost(
            [.. lines.Select(l => (l.Grams, ComponentCostPerGram: find(l.ComponentIngredientId).BufferedCostPerBaseUnit))],
            weightLossPercent);
        return new IngredientCosts(RawMaterialRules.StoredCost(costPerGram), RawMaterialRules.StoredCost(bufferedCostPerGram), 0m);
    }

    /// <summary>
    /// What each ingredient's cost depends on, in the shape IngredientCostGraph takes: a made ingredient's BOM components and
    /// a linked one's linked-to ingredient. Both are edges of one graph, so the two are ordered together.
    /// </summary>
    public static Dictionary<int, IReadOnlyList<int>> Dependencies(IEnumerable<RawMaterial> ingredients, IEnumerable<IngredientBomLine> bomLines) =>
        bomLines
            .Select(l => (l.IngredientId, DependencyId: l.ComponentIngredientId))
            .Concat(ingredients
                .Where(m => m is { Source: RawMaterialSource.Linked, LinkedToId: not null })
                .Select(m => (IngredientId: m.Id, DependencyId: m.LinkedToId!.Value)))
            .GroupBy(e => e.IngredientId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<int>)[.. g.Select(e => e.DependencyId)]);

    /// <summary>
    /// The materials a change to <paramref name="materialId"/>'s cost reaches: itself, and every made and linked
    /// ingredient depending on it, however deeply, the ones <see cref="Reprice"/> works out again (the Price Simulator report's
    /// "price changes applied" to a product).
    /// </summary>
    public static IReadOnlySet<int> ReachedBy(IEnumerable<RawMaterial> materials, IEnumerable<IngredientBomLine> bomLines, int materialId) =>
        ReachedBy(materials, bomLines, [materialId])[materialId];

    /// <summary>
    /// <see cref="ReachedBy(IEnumerable{RawMaterial}, IEnumerable{IngredientBomLine}, int)"/> for each of
    /// <paramref name="materialIds"/>, by id, from one graph: what a list of price changes, or every material in Reports'
    /// Cost Drivers, asks for.
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlySet<int>> ReachedBy(
        IEnumerable<RawMaterial> materials, IEnumerable<IngredientBomLine> bomLines, IReadOnlyCollection<int> materialIds)
    {
        var dependents = IngredientCostGraph.DependentsOf(Dependencies(materials, bomLines));
        Dictionary<int, IReadOnlySet<int>> reached = [];
        foreach (var materialId in materialIds)
        {
            if (!reached.ContainsKey(materialId))
            {
                var set = IngredientCostGraph.GetDependents([materialId], dependents);
                set.Add(materialId);
                reached[materialId] = set;
            }
        }

        return reached;
    }

    /// <summary>
    /// What <paramref name="materials"/> would cost with <paramref name="newCosts"/> (purchased materials' true and buffered
    /// costs per base unit, by id), worked out as a save would cascade them: copies of the changed materials with their new
    /// costs, and copies of every made and linked ingredient depending on them, recomputed in dependency order. Only those
    /// copies are returned, and nothing given is changed. An ingredient the change doesn't reach keeps its stored cost, so
    /// anything priced without the copies prices as it does now.
    /// </summary>
    public static IReadOnlyDictionary<int, RawMaterial> Reprice(
        IReadOnlyDictionary<int, RawMaterial> materials,
        IReadOnlyCollection<IngredientBomLine> bomLines,
        IReadOnlyDictionary<int, (decimal Cost, decimal BufferedCost)> newCosts)
    {
        var copies = new Dictionary<int, RawMaterial>();
        foreach (var (id, (cost, bufferedCost)) in newCosts)
        {
            var copy = materials[id].ShallowCopy();
            copy.CostPerBaseUnit = cost;
            copy.BufferedCostPerBaseUnit = bufferedCost;
            copies[id] = copy;
        }

        var linesByIngredientId = bomLines.ToLookup(l => l.IngredientId);
        foreach (var id in IngredientCostGraph.GetDependentsInOrder(newCosts.Keys, Dependencies(materials.Values, bomLines)))
        {
            var copy = materials[id].ShallowCopy();
            (copy.CostPerBaseUnit, copy.BufferedCostPerBaseUnit, copy.CostBufferPercent) =
                Recompute(copy, linesByIngredientId[id], other => copies.GetValueOrDefault(other) ?? materials[other]);
            copies[id] = copy;
        }

        return copies;
    }
}
