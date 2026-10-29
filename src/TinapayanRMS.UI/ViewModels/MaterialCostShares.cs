using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What a raw material makes up of each product's material cost (its ingredients and packaging for one sellable unit, at
/// buffered costs), for Raw Materials' Used By tiles (2026-10-29, at the user's request: "flour is 38% of Pandesal's cost").
/// Worked out the way the Price Simulator prices a change: every product priced with the material at no cost
/// (IngredientCostRules.Reprice, which carries it into the made and linked ingredients it's in) against as it is. Costs
/// go up with prices in step, so the difference is exactly what the material brings, however it gets there: a line, an
/// add-in, a made or linked ingredient, a packaging set, or the products a composite holds.
/// </summary>
public sealed class MaterialCostShares(
    IProductRepository productRepository,
    IRawMaterialRepository rawMaterialRepository,
    IIngredientBomLineRepository bomLineRepository,
    SavedProductReader savedProducts)
{
    /// <summary>Each of <paramref name="productIds"/>'s share of its material cost, from 0 to 1, by product; a product it can't price is left out.</summary>
    public async Task<IReadOnlyDictionary<int, decimal>> ReadAsync(int materialId, IReadOnlyList<int> productIds, CancellationToken cancellationToken)
    {
        var materials = (await rawMaterialRepository.GetAllAsync(cancellationToken)).ToDictionary(m => m.Id);
        if (!materials.ContainsKey(materialId) || productIds.Count == 0)
        {
            return new Dictionary<int, decimal>();
        }

        var bomLines = await bomLineRepository.GetAllAsync(cancellationToken);

        // A composite is priced with the products it holds, which needn't use the material themselves: those are read too.
        var products = (await productRepository.GetByIdsAsync(productIds, cancellationToken)).ToDictionary(p => p.Id);
        var details = new Dictionary<int, ProductDetail>();
        List<int> toRead = [.. productIds.Distinct()];
        while (toRead.Count > 0)
        {
            foreach (var (id, detail) in await savedProducts.ReadDetailsAsync(toRead, cancellationToken))
            {
                details[id] = detail;
            }

            toRead = [.. details.Values.SelectMany(d => d.Components).Select(c => c.Component.ComponentProductId).Distinct().Where(id => !details.ContainsKey(id))];
        }

        var withoutIt = IngredientCostRules.Reprice(materials, [.. bomLines], new Dictionary<int, (decimal, decimal)> { [materialId] = (0m, 0m) });
        var pricedWithoutIt = SavedProductReader.PricedAt(details, withoutIt);

        var shares = new Dictionary<int, decimal>();
        foreach (var id in productIds.Where(id => products.ContainsKey(id) && details.ContainsKey(id)))
        {
            var now = SavedProductReader.Build(products[id], details[id], CostBasis.Buffered).Costs.Material;
            var without = SavedProductReader.Build(products[id], pricedWithoutIt[id], CostBasis.Buffered, withoutIt).Costs.Material;
            if (now > 0m)
            {
                shares[id] = Math.Clamp((now - without) / now, 0m, 1m);
            }
        }

        return shares;
    }

    /// <summary>"38% of materials", "Under 0.1% of materials" for a trace; null with no share worked out.</summary>
    public static string? Text(decimal? share) => share switch
    {
        null => null,
        < 0.001m => "Under 0.1% of materials",
        _ => string.Create(CultureInfo.CurrentCulture, $"{share.Value * 100m:0.#}% of materials"),
    };
}
