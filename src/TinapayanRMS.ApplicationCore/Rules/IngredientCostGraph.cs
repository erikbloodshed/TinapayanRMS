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
    public static IReadOnlyList<int> GetDependentsInOrder(int changedIngredientId, IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByIngredientId) =>
        GetDependentsInOrder([changedIngredientId], componentIdsByIngredientId);

    /// <summary>
    /// The same for several changed ingredients at once: every one depending on any of them, each once, in dependency
    /// order, the graph turned round and walked once however many changed (a changed one is in it only when it depends on
    /// another that changed).
    /// </summary>
    public static IReadOnlyList<int> GetDependentsInOrder(IEnumerable<int> changedIngredientIds, IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByIngredientId)
    {
        var affected = GetDependents(changedIngredientIds, DependentsOf(componentIdsByIngredientId));
        return affected.Count == 0 ? [] : InDependencyOrder(affected, componentIdsByIngredientId);
    }

    /// <summary>
    /// The graph turned round: each ingredient's direct dependents, the made ingredients it's a component of and the
    /// linked ones cut from it. Made once for many walks (<see cref="GetDependents"/>).
    /// </summary>
    public static IReadOnlyDictionary<int, IReadOnlyList<int>> DependentsOf(IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByIngredientId)
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

        return directDependents.ToDictionary(e => e.Key, e => (IReadOnlyList<int>)e.Value);
    }

    /// <summary>
    /// Every ingredient depending on any of <paramref name="changedIngredientIds"/>, however deeply, in no order, from the
    /// graph turned round (<see cref="DependentsOf"/>). Each is visited once, so a cycle can't loop it.
    /// </summary>
    public static HashSet<int> GetDependents(IEnumerable<int> changedIngredientIds, IReadOnlyDictionary<int, IReadOnlyList<int>> dependentsByIngredientId)
    {
        var affected = new HashSet<int>();
        var queue = new Queue<int>(changedIngredientIds);

        while (queue.TryDequeue(out var current))
        {
            if (!dependentsByIngredientId.TryGetValue(current, out var dependents))
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

        return affected;
    }

    /// <summary>
    /// <paramref name="ingredientIds"/> in dependency order: each after every one of the others it's made from or linked
    /// to, however far down, so recomputing them left to right always sees fresh inputs. What recomputing every made and
    /// linked ingredient at once takes (IngredientCostPropagator.RecomputeAllAsync); a dependency outside the set is
    /// taken as already correct. The same cycle guard as GetDependentsInOrder's.
    /// </summary>
    public static IReadOnlyList<int> InDependencyOrder(IEnumerable<int> ingredientIds, IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByIngredientId)
    {
        var included = ingredientIds as IReadOnlySet<int> ?? ingredientIds.ToHashSet();
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
                    if (included.Contains(componentId))
                    {
                        Visit(componentId);
                    }
                }
            }

            visited.Add(ingredientId);
            order.Add(ingredientId);
        }

        foreach (var ingredientId in included)
        {
            Visit(ingredientId);
        }

        return order;
    }
}
