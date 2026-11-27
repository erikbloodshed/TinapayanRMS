using System.Globalization;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Presentation.Products;

/// <summary>
/// What the Products list's Apply Costing Defaults asks before it sets each product's costing to its category's costing
/// defaults (CostingRules.DefaultCostingFor): the percentages it sets, and when the products' categories come to different
/// ones, which categories take which; and what it then writes (<see cref="Plan"/>), from the same products and lists.
/// </summary>
public static class CostingDefaultsQuestion
{
    /// <summary>
    /// The question for <paramref name="products"/>, named as <paramref name="productNames"/>, with Settings'
    /// <paramref name="defaults"/> and <paramref name="categories"/> as read for it: "Set the costing of Pandesal to the
    /// costing defaults: labor 35%, …?", the Breads category's when it sets its own, or a line for each set of percentages
    /// their categories come to.
    /// </summary>
    public static string For(string productNames, IReadOnlyList<Product> products, CostingDefaults defaults, IReadOnlyList<ProductCategory> categories)
    {
        var categoriesById = categories.ToDictionary(c => c.Id);
        var sets = products
            .Select(p => p.CategoryId is { } id ? categoriesById.GetValueOrDefault(id) : null)
            .DistinctBy(c => c?.Id)
            .Select(category => (Category: category, Costing: CostingRules.DefaultCostingFor(defaults, category)))
            .GroupBy(c => Percentages(c.Costing))
            .Select(set => (Text: set.Key, Categories: set.Select(c => c.Category).ToList()))
            .OrderBy(set => set.Categories.Any(c => c is not null && CostingRules.SetsOwnCosting(c)) ? 0 : 1)
            .ThenBy(set => Label(set.Categories), StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        const string versions = "\n\nA version is recorded of each one this changes.";
        if (sets.Count == 1)
        {
            var own = sets[0].Categories.Where(c => c is not null && CostingRules.SetsOwnCosting(c)).ToList();
            var source = own.Count switch
            {
                0 => "the costing defaults",
                1 when sets[0].Categories.Count == 1 => $"the {own[0]!.Name} category's costing defaults",
                _ => "their categories' costing defaults",
            };
            return $"Set the costing of {productNames} to {source}: {sets[0].Text}?{versions}";
        }

        return $"Set the costing of {productNames} to their categories' costing defaults?\n\n"
            + string.Join("\n", sets.Select(set => $"{Label(set.Categories)}: {set.Text}."))
            + versions;
    }

    /// <summary>
    /// What Apply Costing Defaults writes, once the question is answered: each of <paramref name="products"/> with its
    /// category's costing defaults (CostingRules.DefaultCostingFor), as the question names them.
    /// </summary>
    public static IReadOnlyList<(int ProductId, CostingParameters Costing)> Plan(
        IReadOnlyList<Product> products, CostingDefaults defaults, IReadOnlyList<ProductCategory> categories)
    {
        var categoriesById = categories.ToDictionary(c => c.Id);
        return [.. products.Select(p => (p.Id, CostingRules.DefaultCostingFor(defaults, p.CategoryId is { } id ? categoriesById.GetValueOrDefault(id) : null)))];
    }

    /// <summary>"labor 35%, overhead 25%, administrative 15% and markup 30%".</summary>
    private static string Percentages(CostingParameters costing) => string.Create(
        CultureInfo.CurrentCulture,
        $"labor {costing.LaborPercent:0.##}%, overhead {costing.OverheadPercent:0.##}%, administrative {costing.AdministrativePercent:0.##}% and markup {costing.MarkupPercent:0.##}%");

    /// <summary>The categories taking one set, by name, "Uncategorized" for no category: "Breads, Cakes and Uncategorized".</summary>
    private static string Label(IReadOnlyList<ProductCategory?> categories)
    {
        List<string> names =
        [
            .. categories.OfType<ProductCategory>().Select(c => c.Name).Order(StringComparer.CurrentCultureIgnoreCase),
            .. categories.Any(c => c is null) ? ["Uncategorized"] : Array.Empty<string>(),
        ];
        return names.Count == 1 ? names[0] : $"{string.Join(", ", names[..^1])} and {names[^1]}";
    }
}
