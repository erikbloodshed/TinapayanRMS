using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UseCases.SubAssemblies;

/// <summary>
/// What saving new lines for a standalone sub-assembly does to the products linking it (Cheese Roll +0.42): each priced as
/// saved, and again with the new lines in place of the sub-assembly's, at buffered costs. The products using a made
/// ingredient made from it change too, which the screen's question says without pricing them.
/// </summary>
public sealed class SharedRecipeImpact(
    ISubAssemblyRepository subAssemblyRepository,
    IProductRepository productRepository,
    SavedProductReader savedProducts)
{
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
            // A change that shows: to the centavo, rounded half away from zero as a shown amount is.
            if (Math.Round(before, 2, MidpointRounding.AwayFromZero) != Math.Round(after, 2, MidpointRounding.AwayFromZero))
            {
                impacts.Add(new RecipeImpact(product.Name, before, after));
            }
        }

        return [.. impacts.OrderByDescending(i => Math.Abs(i.Change)).ThenBy(i => i.ProductName, StringComparer.CurrentCultureIgnoreCase)];
    }
}
