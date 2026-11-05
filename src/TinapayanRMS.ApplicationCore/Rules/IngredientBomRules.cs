namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// A made ingredient's bill of materials: the lines of the sub-assembly it's made from and its own add-ins, less its
/// weight loss.
/// </summary>
public static class IngredientBomRules
{
    /// <summary>
    /// A made ingredient's add-ins, its own extra ingredients on top of <paramref name="subAssemblyName"/>'s recipe, as a
    /// product's linked usage has: none is fine, and each needs positive grams, comes once, and isn't already in the recipe.
    /// <paramref name="baseIngredientIds"/> are the sub-assembly's lines' ingredients.
    /// </summary>
    public static void EnsureValidAddIns(
        IReadOnlyList<(int IngredientId, decimal Grams, string IngredientName)> addIns,
        string subAssemblyName,
        IReadOnlyCollection<int> baseIngredientIds) =>
        AddInRules.EnsureValid(
            addIns,
            baseIngredientIds,
            alreadyThere => $"{alreadyThere} is already in {subAssemblyName}. Add-ins are only for what its recipe doesn't have — "
                + $"change how much it uses on the Sub-Assemblies screen instead.");

    /// <summary>
    /// The plainest loop, named plainly: a made ingredient that's a line of the sub-assembly it's made from. Butter Icing
    /// can't be made from a recipe with Butter Icing in it. A longer loop is EnsureNoCycle's.
    /// </summary>
    public static void EnsureNotInOwnRecipe(int madeIngredientId, string madeIngredientName, string subAssemblyName, IReadOnlyCollection<int> lineIngredientIds)
    {
        if (lineIngredientIds.Contains(madeIngredientId))
        {
            throw new BusinessRuleException(
                $"{madeIngredientName} is made from {subAssemblyName}, so it can't be one of {subAssemblyName}'s ingredients.");
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
