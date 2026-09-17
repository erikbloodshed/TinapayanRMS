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

    /// <summary>
    /// A product has one sub-assembly in each role, its own and the ones it links together: its recipe's
    /// sections are called by their roles, so two in one role couldn't be told apart.
    /// </summary>
    public static void EnsureOnePerRole(IReadOnlyList<(int RoleId, string RoleName)> roles)
    {
        var repeated = roles.GroupBy(r => r.RoleId).FirstOrDefault(g => g.Count() > 1);
        if (repeated is not null)
        {
            throw new InvalidOperationException(
                $"This recipe has more than one sub-assembly in the {repeated.First().RoleName} role. A product takes one in each role: give one of them another role, or take it out.");
        }
    }

    /// <summary>
    /// What a sub-assembly is called where it isn't plain whose it is: a standalone one by its name, and a
    /// product's own, which has no name, as that product's role, e.g. "Cream Loaf's Dough". Needs its Role and,
    /// for a product's own, its OwnerProduct.
    /// </summary>
    public static string DisplayName(SubAssembly subAssembly) =>
        subAssembly.OwnerProductId is null
            ? subAssembly.Name ?? string.Empty
            : $"{subAssembly.OwnerProduct?.Name ?? "Another product"}'s {subAssembly.Role?.Name ?? "sub-assembly"}";

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
