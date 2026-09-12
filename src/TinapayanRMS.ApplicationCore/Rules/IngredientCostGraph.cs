namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// Pure graph logic for propagating a cost change through the made-ingredient BOM
/// graph. Kept separate from IngredientCostPropagator (Infrastructure) so the
/// ordering logic is testable without a database.
/// </summary>
public static class IngredientCostGraph
{
    /// <summary>
    /// Every made ingredient that transitively uses changedIngredientId as a
    /// component (directly or through another made ingredient), in dependency
    /// order — a component always comes before anything that uses it, so
    /// recomputing them in this order left-to-right always sees fresh inputs.
    /// componentIdsByIngredientId maps a made ingredient's id to its direct
    /// components' ids (purchased ingredients and ones with no BOM don't appear
    /// as keys). Cycles can't occur here — they're rejected at BOM-save time —
    /// but the visiting guard is cheap insurance against a data-integrity bug
    /// turning into an infinite loop.
    /// </summary>
    public static IReadOnlyList<int> GetDependentsInOrder(int changedIngredientId, IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByIngredientId)
    {
        var directDependents = new Dictionary<int, List<int>>();
        foreach (var (ingredientId, componentIds) in componentIdsByIngredientId)
        {
            foreach (var componentId in componentIds)
            {
                if (!directDependents.TryGetValue(componentId, out var dependents))
                {
                    dependents = [];
                    directDependents[componentId] = dependents;
                }

                dependents.Add(ingredientId);
            }
        }

        var affected = new HashSet<int>();
        var queue = new Queue<int>();
        queue.Enqueue(changedIngredientId);

        while (queue.TryDequeue(out var current))
        {
            if (!directDependents.TryGetValue(current, out var dependents))
            {
                continue;
            }

            foreach (var dependent in dependents)
            {
                if (affected.Add(dependent))
                {
                    queue.Enqueue(dependent);
                }
            }
        }

        if (affected.Count == 0)
        {
            return [];
        }

        var order = new List<int>();
        var visited = new HashSet<int>();
        var visiting = new HashSet<int>();

        void Visit(int ingredientId)
        {
            if (visited.Contains(ingredientId) || !visiting.Add(ingredientId))
            {
                return;
            }

            if (componentIdsByIngredientId.TryGetValue(ingredientId, out var components))
            {
                foreach (var componentId in components)
                {
                    if (affected.Contains(componentId))
                    {
                        Visit(componentId);
                    }
                }
            }

            visited.Add(ingredientId);
            order.Add(ingredientId);
        }

        foreach (var ingredientId in affected)
        {
            Visit(ingredientId);
        }

        return order;
    }
}
