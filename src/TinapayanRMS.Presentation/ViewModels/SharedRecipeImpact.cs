using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A product whose base cost a sub-assembly's new lines move, for one sellable unit.</summary>
public sealed record RecipeImpact(string ProductName, decimal BaseCostBefore, decimal BaseCostAfter)
{
    public decimal Change => BaseCostAfter - BaseCostBefore;
}

/// <summary>
/// What saving new lines for a standalone sub-assembly does to the products linking it (Cheese Roll +0.42): each
/// priced as saved, and again with the new lines in
/// place of the sub-assembly's, at buffered costs. The products using a made ingredient made from it change too, which
/// the question says without pricing them.
/// </summary>
public sealed class SharedRecipeImpact(
    ISubAssemblyRepository subAssemblyRepository,
    IProductRepository productRepository,
    SavedProductReader savedProducts)
{
    /// <summary>How many products the question names; the rest are counted.</summary>
    private const int Named = 8;

    /// <summary>The products linking <paramref name="subAssemblyId"/> whose base cost <paramref name="newLines"/> move, the most moved first; deleted ones left out.</summary>
    public async Task<IReadOnlyList<RecipeImpact>> ReadAsync(int subAssemblyId, IReadOnlyList<SubAssemblyLine> newLines, CancellationToken cancellationToken)
    {
        var linkedBy = await subAssemblyRepository.GetStandaloneLinkedByAsync(cancellationToken);
        if (!linkedBy.TryGetValue(subAssemblyId, out var linking) || linking.Count == 0)
        {
            return [];
        }

        List<Product> products = [.. (await productRepository.GetByIdsAsync([.. linking.Select(l => l.Id).Distinct()], cancellationToken)).Where(p => !p.IsDeleted)];
        if (products.Count == 0)
        {
            return [];
        }

        var details = await savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], cancellationToken);
        List<RecipeImpact> impacts = [];
        foreach (var product in products)
        {
            var detail = details[product.Id];
            var before = SavedProductReader.Build(product, detail).Costs.BaseCost;
            var withNewLines = detail with { SubAssemblyLines = [.. detail.SubAssemblyLines.Where(l => l.SubAssemblyId != subAssemblyId), .. newLines] };
            var after = SavedProductReader.Build(product, withNewLines).Costs.BaseCost;
            if (Math.Round(before, 2) != Math.Round(after, 2))
            {
                impacts.Add(new RecipeImpact(product.Name, before, after));
            }
        }

        return [.. impacts.OrderByDescending(i => Math.Abs(i.Change)).ThenBy(i => i.ProductName, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>
    /// The question Save asks: "Saving Sweet Dough changes what 2 products cost to make, for one sellable unit: Cheese Roll
    /// 18.48 → 18.90 (+0.42) …", naming the first eight, and saying when made ingredients made from it change too.
    /// </summary>
    public static string Question(string subAssemblyName, IReadOnlyList<RecipeImpact> impacts, int madeIngredientCount)
    {
        var lines = impacts.Take(Named).Select(i => string.Create(
            CultureInfo.CurrentCulture, $"{i.ProductName}: {i.BaseCostBefore:N2} → {i.BaseCostAfter:N2} ({i.Change:+0.00;-0.00})"));
        var more = impacts.Count > Named ? $"\nand {impacts.Count - Named} more" : string.Empty;
        var made = madeIngredientCount > 0
            ? $"\n\nThe {(madeIngredientCount == 1 ? "made ingredient" : $"{madeIngredientCount} made ingredients")} made from it change too, and the products using them."
            : string.Empty;
        var what = impacts.Count == 1 ? "1 product" : $"{impacts.Count} products";
        return $"Saving {subAssemblyName} changes the base cost of {what}, for one sellable unit:\n\n{string.Join("\n", lines)}{more}{made}\n\nSave it?";
    }
}
