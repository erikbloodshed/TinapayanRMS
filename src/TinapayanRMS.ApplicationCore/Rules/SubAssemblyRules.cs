using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Rules;

public static class SubAssemblyRules
{
    /// <summary>A sub-assembly needs at least one line, positive grams, and no ingredient used twice.</summary>
    public static void EnsureValidLines(IReadOnlyList<RecipeLine> lines)
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

        // The base is 100% between its lines by definition, so a base line is always given in grams.
        if (lines.Any(l => l.IsFormulaBase && l.AuthoredBakersPercent is not null))
        {
            throw new InvalidOperationException("A base line is given in grams, not as a percentage.");
        }

        if (lines.Any(l => l.AuthoredBakersPercent is <= 0))
        {
            throw new InvalidOperationException("A sub-assembly line's baker's percentage must be positive.");
        }
    }

    /// <summary>
    /// A packaging set needs at least one line, a positive quantity per set, and no material used twice. The
    /// materials' type is checked where they're loaded (RawMaterialRules.EnsureUsableAsPackaging), and each
    /// line's unit is checked there too: it must be one of that material's own units, or null for its base unit.
    /// </summary>
    public static void EnsureValidPackagingLines(IReadOnlyList<(int PackagingMaterialId, int? UnitId, decimal QtyPerSet)> lines)
    {
        if (lines.Count == 0)
        {
            throw new InvalidOperationException("A packaging set needs at least one line.");
        }

        if (lines.Any(l => l.QtyPerSet <= 0))
        {
            throw new InvalidOperationException("A packaging set line's quantity must be positive.");
        }

        if (lines.Select(l => l.PackagingMaterialId).Distinct().Count() != lines.Count)
        {
            throw new InvalidOperationException("A packaging set can't use the same material more than once — put its quantity on a single line instead.");
        }
    }

    /// <summary>
    /// A saved sub-assembly keeps its kind: a recipe (ingredients in grams) can't move to the Packaging role, nor a
    /// packaging set to a recipe role, since its lines are of the other kind and products use the two differently.
    /// </summary>
    public static void EnsureKindUnchanged(bool wasPackaging, bool isPackaging)
    {
        if (wasPackaging != isPackaging)
        {
            throw new InvalidOperationException(wasPackaging
                ? "A packaging set stays in the Packaging role. Make a new sub-assembly for a recipe instead."
                : "A recipe can't move to the Packaging role. Make a new sub-assembly for a packaging set instead.");
        }
    }

    /// <summary>
    /// A recipe's sub-assemblies are ingredients in grams, so none of them takes the Packaging role: a product's
    /// packaging is its Packaging tab, which can link a packaging set instead.
    /// </summary>
    public static void EnsureRecipeRole(SubAssemblyRole role)
    {
        if (role.IsPackaging)
        {
            throw new InvalidOperationException(
                $"The {role.Name} role is for packaging sets on the Sub-Assemblies screen, not a recipe section. A product takes one as its packaging: Add Sub-Assembly in the Packaging role.");
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
    /// The formula's 100% base: the weight of the lines ticked as base, which is what every line's baker's
    /// percentage is read against. Usually the flour, and more than one flour ticks together to make 100%
    /// between them. Nothing ticked gives 0, which means the formula has no base.
    /// </summary>
    public static decimal FormulaBaseWeight(IReadOnlyList<(decimal Grams, bool IsFormulaBase)> lines) =>
        lines.Where(l => l.IsFormulaBase).Sum(l => l.Grams);

    /// <summary>
    /// A line in baker's percentage: its weight against the formula's base (<see cref="FormulaBaseWeight"/>),
    /// so the base itself comes to 100 and 650 g of water on 1,000 g of flour comes to 65. Null when nothing
    /// is ticked — a glaze or a filling needn't have a base, and no figure is the honest answer, not zero.
    /// </summary>
    public static decimal? CalculateBakersPercent(decimal grams, decimal baseWeight) =>
        baseWeight <= 0 ? null : grams / baseWeight * 100m;

    /// <summary>
    /// A line in absolute percentage: its weight against the whole batch, so every line together comes to 100 and 150 g
    /// of butter in a 1,000 g glaze comes to 15. What a formula with nothing ticked as its base shows instead of baker's
    /// percentage. Null for an empty batch.
    /// </summary>
    public static decimal? CalculateAbsolutePercent(decimal grams, decimal batchWeight) =>
        batchWeight <= 0 ? null : grams / batchWeight * 100m;

    /// <summary>
    /// The other way: a line's grams from its baker's percentage, so 65% on 1,000 g of flour is 650 g. What a line typed as
    /// a percentage is worked out from, again, whenever the base weight changes. Rounded to the 4 decimals grams are stored
    /// in, so a line saved this way reads back exactly as it was worked out — unrounded, reopening it could move its grams
    /// by a ten-thousandth and make a recipe nobody touched look changed.
    /// </summary>
    public static decimal GramsFromBakersPercent(decimal percent, decimal baseWeight) =>
        Math.Round(baseWeight * percent / 100m, 4);

    /// <summary>The longest name a sub-assembly can have (the Name column's length).</summary>
    public const int MaxNameLength = 100;

    /// <summary>A name as it's saved: trimmed, and null when nothing's left.</summary>
    public static string? NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>
    /// What a sub-assembly first written in a product's Recipe Structure is saved as on the Sub-Assemblies screen: the
    /// product and its role, "Cheese Roll Dough", and the section's own name after them when it has one that isn't just
    /// the role's, "Cheese Roll Filling: Cheese". Not yet unique (<see cref="UniqueStandaloneName"/>) nor cut to length.
    /// </summary>
    public static string StandaloneNameFor(string productName, string roleName, string? sectionName)
    {
        var baseName = $"{productName.Trim()} {roleName.Trim()}";
        return NormalizeName(sectionName) is { } section && !string.Equals(section, roleName.Trim(), StringComparison.OrdinalIgnoreCase)
            ? $"{baseName}: {section}"
            : baseName;
    }

    /// <summary>
    /// <paramref name="name"/>, or the first of "name 2", "name 3", … that no sub-assembly in <paramref name="taken"/> has
    /// (case- and space-insensitively), cut so the whole still fits <see cref="MaxNameLength"/>.
    /// </summary>
    public static string UniqueStandaloneName(string name, IReadOnlyCollection<string> taken)
    {
        var names = taken.Select(n => n.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var trimmed = name.Trim();
        for (var n = 1; ; n++)
        {
            var suffix = n == 1 ? string.Empty : $" {n}";
            var stem = trimmed.Length + suffix.Length > MaxNameLength ? trimmed[..(MaxNameLength - suffix.Length)].TrimEnd() : trimmed;
            if (!names.Contains(stem + suffix))
            {
                return stem + suffix;
            }
        }
    }

    /// <summary>
    /// What a linked sub-assembly goes by beside its role in a product's recipe, once the recipe has more than one in
    /// that role: its name, less the "Cheese Roll Filling: " it was saved under by that product
    /// (<see cref="StandaloneNameFor"/>), so its tab reads "Filling: Cheese"; and nothing when it's just "Cheese Roll
    /// Filling", which was saved from that product without a name of its own, so it goes by its role alone there, as it
    /// did before it was saved.
    /// </summary>
    public static string? SectionLabel(string subAssemblyName, string? productName, string roleName)
    {
        if (NormalizeName(productName) is { } product)
        {
            var saved = StandaloneNameFor(product, roleName, null);
            if (string.Equals(subAssemblyName.Trim(), saved, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var prefix = saved + ": ";
            if (subAssemblyName.Length > prefix.Length && subAssemblyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return subAssemblyName[prefix.Length..].Trim();
            }
        }

        return subAssemblyName;
    }

    /// <summary>
    /// A product can have several sub-assemblies in one role (an ube filling and a cheese filling), its own and the ones
    /// it links together, as long as they can be told apart: within a role no two go by the same name
    /// (<see cref="SectionLabel"/>, case- and space-insensitively), and at most one goes without one. Its tab reads
    /// "Filling: Ube" beside "Filling: Cheese", or "Filling" beside "Filling: Cheese".
    /// </summary>
    /// <param name="recipe">Whose recipe the message is about: "This recipe", or a product's.</param>
    public static void EnsureSectionsDistinct(IReadOnlyList<(int RoleId, string RoleName, string? Label)> sections, string recipe = "This recipe")
    {
        foreach (var role in sections.GroupBy(s => s.RoleId))
        {
            var clash = role.GroupBy(s => NormalizeName(s.Label), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (clash is null)
            {
                continue;
            }

            var roleName = role.First().RoleName;
            throw new InvalidOperationException(clash.Key is null
                ? $"{recipe} has more than one {roleName} without a name. Name them, such as Ube and Cheese, so their tabs can be told apart."
                : $"{recipe} has two {roleName} sub-assemblies called {clash.Key}. Give one of them another name.");
        }
    }

    /// <summary>The name typed for a new recipe section is optional, and at most <see cref="MaxNameLength"/> characters.</summary>
    public static void EnsureValidSectionName(string? name)
    {
        if (NormalizeName(name) is { Length: > MaxNameLength } tooLong)
        {
            throw new InvalidOperationException($"'{tooLong[..20]}…' is too long for a sub-assembly's name: keep it to {MaxNameLength} characters.");
        }
    }

    /// <summary>
    /// A sub-assembly needs a name no other has (case- and space-insensitively): products pick them by name.
    /// <paramref name="otherStandaloneNames"/> leaves out the one being saved.
    /// </summary>
    public static void EnsureStandaloneNameAvailable(string name, IReadOnlyCollection<string> otherStandaloneNames)
    {
        var trimmed = name.Trim();
        if (otherStandaloneNames.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"There's already a sub-assembly named '{trimmed}'. Give this one its own name.");
        }
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
