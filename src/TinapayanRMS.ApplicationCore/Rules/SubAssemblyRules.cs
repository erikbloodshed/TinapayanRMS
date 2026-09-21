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

    /// <summary>A name as it's saved: trimmed, and null when nothing's left, since a product's own needn't have one.</summary>
    public static string? NormalizeName(string? name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

    /// <summary>
    /// The name a recipe section goes by beside its role, once the recipe has more than one in that role: a product's
    /// own by its name, which it needn't have, and a linked one by its name, or else by its owner product's.
    /// Needs a linked one's OwnerProduct.
    /// </summary>
    public static string? SectionLabel(SubAssembly subAssembly, int productId) =>
        subAssembly.OwnerProductId == productId
            ? NormalizeName(subAssembly.Name)
            : NormalizeName(subAssembly.Name) ?? subAssembly.OwnerProduct?.Name ?? "another product";

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

    /// <summary>
    /// A product's own sub-assemblies' names: optional, at most <see cref="MaxNameLength"/> characters, and no two alike
    /// (case- and space-insensitively), whatever their roles, as the database keeps them unique within their product.
    /// </summary>
    public static void EnsureValidOwnedNames(IReadOnlyList<string?> names)
    {
        var named = names.Select(NormalizeName).OfType<string>().ToList();
        if (named.FirstOrDefault(n => n.Length > MaxNameLength) is { } tooLong)
        {
            throw new InvalidOperationException($"'{tooLong[..20]}…' is too long for a sub-assembly's name: keep it to {MaxNameLength} characters.");
        }

        if (named.GroupBy(n => n, StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1) is { } repeated)
        {
            throw new InvalidOperationException($"This product has two sub-assemblies of its own named {repeated.Key}. Give one of them another name.");
        }
    }

    /// <summary>
    /// What a sub-assembly is called where it isn't plain whose it is: a standalone one by its name, and a
    /// product's own as that product's role, with its name when it has one, e.g. "Cream Loaf's Dough" or
    /// "Cream Loaf's Filling: Ube". Needs its Role and, for a product's own, its OwnerProduct.
    /// </summary>
    public static string DisplayName(SubAssembly subAssembly) =>
        subAssembly.OwnerProductId is null
            ? subAssembly.Name ?? string.Empty
            : $"{subAssembly.OwnerProduct?.Name ?? "Another product"}'s {subAssembly.Role?.Name ?? "sub-assembly"}"
                + (NormalizeName(subAssembly.Name) is { } name ? $": {name}" : string.Empty);

    /// <summary>
    /// A standalone sub-assembly needs a name no other standalone one has (case- and space-insensitively):
    /// products pick them by name. <paramref name="otherStandaloneNames"/> leaves out the one being saved.
    /// </summary>
    public static void EnsureStandaloneNameAvailable(string name, IReadOnlyCollection<string> otherStandaloneNames)
    {
        var trimmed = name.Trim();
        if (otherStandaloneNames.Any(n => string.Equals(n.Trim(), trimmed, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"There's already a standalone sub-assembly named '{trimmed}'. Give this one its own name.");
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
