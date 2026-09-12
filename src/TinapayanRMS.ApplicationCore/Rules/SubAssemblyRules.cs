namespace TinapayanRMS.ApplicationCore.Rules;

public static class SubAssemblyRules
{
    /// <summary>A sub-assembly needs at least one line, positive grams, and no ingredient used twice.</summary>
    public static void EnsureValidLines(IReadOnlyList<(int IngredientId, decimal Grams)> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A sub-assembly needs at least one line.");
        }

        if (lines.Any(l => l.Grams <= 0))
        {
            throw new InvalidOperationException("A sub-assembly line's grams must be positive.");
        }

        if (lines.Select(l => l.IngredientId).Distinct().Count() != lines.Count)
        {
            throw new InvalidOperationException("A sub-assembly can't use the same ingredient more than once — combine it into a single line instead.");
        }
    }

    /// <summary>TotalWeight = Σ grams; TotalCost = Σ (grams × that line's ingredient CostPerGram).</summary>
    public static (decimal TotalWeight, decimal TotalCost) CalculateTotals(IReadOnlyList<(decimal Grams, decimal IngredientCostPerGram)> lines)
    {
        var totalWeight = lines.Sum(l => l.Grams);
        var totalCost = lines.Sum(l => l.Grams * l.IngredientCostPerGram);
        return (totalWeight, totalCost);
    }
}
