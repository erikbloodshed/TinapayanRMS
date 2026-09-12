namespace TinapayanRMS.ApplicationCore;

public static class IngredientBomRules
{
    /// <summary>A made ingredient needs at least one line, positive grams, and no component used twice.</summary>
    public static void EnsureValidLines(IReadOnlyList<(int ComponentIngredientId, decimal Grams)> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A made ingredient needs at least one BOM line.");
        }

        if (lines.Any(l => l.Grams <= 0))
        {
            throw new InvalidOperationException("A BOM line's grams must be positive.");
        }

        if (lines.Select(l => l.ComponentIngredientId).Distinct().Count() != lines.Count)
        {
            throw new InvalidOperationException("A made ingredient can't use the same component ingredient more than once — combine it into a single line instead.");
        }
    }

    /// <summary>
    /// An ingredient can't appear anywhere in its own BOM tree. componentIdsByIngredientId
    /// is every OTHER made ingredient's current component list (ingredientId's own
    /// about-to-be-replaced lines are excluded by the caller).
    /// </summary>
    public static void EnsureNoCycle(
        int ingredientId,
        IEnumerable<int> newComponentIds,
        IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByIngredientId)
    {
        foreach (var componentId in newComponentIds)
        {
            if (componentId == ingredientId)
            {
                throw new InvalidOperationException("An ingredient can't be a component of itself.");
            }

            if (IsReachable(componentId, ingredientId, componentIdsByIngredientId))
            {
                throw new InvalidOperationException(
                    $"That would create a cycle: ingredient {componentId} already contains ingredient {ingredientId} somewhere in its own BOM.");
            }
        }
    }

    private static bool IsReachable(int fromId, int targetId, IReadOnlyDictionary<int, IReadOnlyList<int>> componentIdsByIngredientId)
    {
        var visited = new HashSet<int>();
        var stack = new Stack<int>();
        stack.Push(fromId);

        while (stack.TryPop(out var current))
        {
            if (current == targetId)
            {
                return true;
            }

            if (!visited.Add(current))
            {
                continue;
            }

            if (componentIdsByIngredientId.TryGetValue(current, out var components))
            {
                foreach (var component in components)
                {
                    stack.Push(component);
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Output grams = Σ line grams × (1 − weightLossPercent / 100); CostPerGram = Σ (line
    /// grams × component CostPerGram) / output grams. lines carry each component's CURRENT
    /// CostPerGram, which the caller must have already up to date (see IngredientCostGraph).
    /// </summary>
    public static (decimal OutputGrams, decimal CostPerGram) CalculateCost(
        IReadOnlyList<(decimal Grams, decimal ComponentCostPerGram)> lines,
        decimal weightLossPercent)
    {
        var totalGrams = lines.Sum(l => l.Grams);
        var totalCost = lines.Sum(l => l.Grams * l.ComponentCostPerGram);
        var outputGrams = totalGrams * (1 - weightLossPercent / 100m);
        var costPerGram = outputGrams > 0 ? totalCost / outputGrams : 0m;
        return (outputGrams, costPerGram);
    }
}
