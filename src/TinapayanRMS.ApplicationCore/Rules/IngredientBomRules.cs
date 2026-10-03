namespace TinapayanRMS.ApplicationCore.Rules;

public static class IngredientBomRules
{
    /// <summary>A made ingredient needs at least one line, positive grams, and no component used twice.</summary>
    public static void EnsureValidLines(IReadOnlyList<(int ComponentIngredientId, decimal Grams)> lines)
    {
        if (lines.Count == 0)
        {
            throw new BusinessRuleException("A made ingredient needs at least one BOM line.");
        }

        if (lines.Any(l => l.Grams <= 0))
        {
            throw new BusinessRuleException("A BOM line's grams must be positive.");
        }

        if (lines.Select(l => l.ComponentIngredientId).Distinct().Count() != lines.Count)
        {
            throw new BusinessRuleException("A made ingredient can't use the same component ingredient more than once — combine it into a single line instead.");
        }
    }

    /// <summary>
    /// An ingredient can't appear anywhere in its own cost tree. componentIdsByIngredientId
    /// is what every OTHER ingredient's cost currently depends on — a made ingredient's BOM
    /// components, and a linked ingredient's linked-to one (ingredientId's own about-to-be-
    /// replaced lines or link are excluded by the caller). newComponentIds are its new BOM
    /// components, or the one ingredient it's being linked to.
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
                throw new BusinessRuleException("An ingredient can't be a component of itself.");
            }

            if (IsReachable(componentId, ingredientId, componentIdsByIngredientId))
            {
                throw new BusinessRuleException(
                    $"That would create a cycle: ingredient {componentId} already uses ingredient {ingredientId} somewhere in its BOM or links.");
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

    /// <summary>What a made ingredient's BOM makes: what goes in, less its weight loss. 1,500 g at 20% makes 1,200 g.</summary>
    public static decimal CalculateOutputGrams(decimal totalGrams, decimal weightLossPercent) =>
        totalGrams * (1 - weightLossPercent / 100m);

    /// <summary>
    /// Output grams = Σ line grams × (1 − weightLossPercent / 100); cost per gram = Σ (line
    /// grams × component cost per gram) / output grams. lines carry each component's CURRENT
    /// cost per gram (its CostPerBaseUnit, or its BufferedCostPerBaseUnit for the buffered rollup),
    /// which the caller must have already up to date (see IngredientCostGraph).
    /// </summary>
    public static (decimal OutputGrams, decimal CostPerGram) CalculateCost(
        IReadOnlyList<(decimal Grams, decimal ComponentCostPerGram)> lines,
        decimal weightLossPercent)
    {
        var totalGrams = lines.Sum(l => l.Grams);
        var totalCost = lines.Sum(l => l.Grams * l.ComponentCostPerGram);
        var outputGrams = CalculateOutputGrams(totalGrams, weightLossPercent);
        var costPerGram = outputGrams > 0 ? totalCost / outputGrams : 0m;
        return (outputGrams, costPerGram);
    }
}
