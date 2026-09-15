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

    /// <summary>TotalWeight = Σ grams; TotalCost = Σ (grams × that line's ingredient cost per gram, its CostPerBaseUnit).</summary>
    public static (decimal TotalWeight, decimal TotalCost) CalculateTotals(IReadOnlyList<(decimal Grams, decimal IngredientCostPerGram)> lines)
    {
        var totalWeight = lines.Sum(l => l.Grams);
        var totalCost = lines.Sum(l => l.Grams * l.IngredientCostPerGram);
        return (totalWeight, totalCost);
    }

    /// <summary>
    /// A product's own sub-assemblies need a name each, and different names: the owner's recipe shows
    /// them side by side.
    /// </summary>
    public static void EnsureValidOwnedNames(IReadOnlyList<string> names)
    {
        if (names.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidOperationException("Every sub-assembly needs a name.");
        }

        var duplicate = names
            .GroupBy(n => n.Trim(), StringComparer.CurrentCultureIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"This product already has a sub-assembly named '{duplicate.Key}'. Give each one its own name.");
        }
    }

    /// <summary>
    /// A standalone sub-assembly needs a name no other standalone one has (case- and space-insensitively):
    /// products pick them by name. <paramref name="otherStandaloneNames"/> leaves out the one being saved.
    /// </summary>
    public static void EnsureStandaloneNameAvailable(string name, IReadOnlyCollection<string> otherStandaloneNames)
    {
        var trimmed = name.Trim();
        if (otherStandaloneNames.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.CurrentCultureIgnoreCase)))
        {
            throw new InvalidOperationException($"There's already a standalone sub-assembly named '{trimmed}'. Give this one its own name.");
        }
    }

    /// <summary>
    /// <paramref name="name"/> if the product has no sub-assembly by that name yet, otherwise the first
    /// free one of "name (copy)", "name (copy 2)", … — for a copy that would otherwise clash.
    /// </summary>
    public static string UniqueName(string name, IReadOnlyCollection<string> takenNames)
    {
        var taken = new HashSet<string>(takenNames.Select(n => n.Trim()), StringComparer.CurrentCultureIgnoreCase);
        var trimmed = name.Trim();
        if (!taken.Contains(trimmed))
        {
            return trimmed;
        }

        var candidate = $"{trimmed} (copy)";
        for (var n = 2; taken.Contains(candidate); n++)
        {
            candidate = $"{trimmed} (copy {n})";
        }

        return candidate;
    }

    /// <summary>
    /// A sub-assembly another product links can't be deleted from under it: that product has to copy it
    /// or remove it first. <paramref name="linkingProductNames"/> are the products whose recipes link it.
    /// </summary>
    public static void EnsureNotLinkedElsewhere(string subAssemblyName, IReadOnlyCollection<string> linkingProductNames)
    {
        if (linkingProductNames.Count == 0)
        {
            return;
        }

        var names = string.Join(", ", linkingProductNames.Order(StringComparer.CurrentCultureIgnoreCase));
        var verb = linkingProductNames.Count == 1 ? "links" : "link";
        throw new InvalidOperationException(
            $"{names} {verb} {subAssemblyName}. Make a copy of it there, or remove it there, first.");
    }
}
