using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing;

/// <summary>
/// Raw material price changes tried without saving them: every product priced with them, as a real change would cascade
/// to made and linked ingredients and the composites holding a product (IngredientCostRules.Reprice, SavedProductReader),
/// and what each change reaches. The Price Simulator screen and the MCP server's simulate_price_changes both work them out
/// here, so the two can't price a change differently.
/// </summary>
public static class PriceChangeSimulation
{
    /// <summary>
    /// <paramref name="products"/> priced at <paramref name="basis"/> with <paramref name="newCosts"/> (purchased materials'
    /// true and buffered costs per base unit, by id) in place of their materials' costs, from <paramref name="details"/>
    /// as read; and, for each changed material, the materials it reaches, itself among them.
    /// </summary>
    /// <param name="details">The products' details, and those of every product a composite among them holds.</param>
    public static SimulatedPrices Run(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<int, ProductDetail> details,
        IReadOnlyDictionary<int, RawMaterial> materials,
        IReadOnlyCollection<IngredientBomLine> bomLines,
        IReadOnlyDictionary<int, (decimal Cost, decimal BufferedCost)> newCosts,
        CostBasis basis,
        CancellationToken cancellationToken = default)
    {
        var repriced = IngredientCostRules.Reprice(materials, bomLines, newCosts);
        var pricedDetails = SavedProductReader.PricedAt(details, repriced);
        Dictionary<int, SavedProduct> simulated = [];
        foreach (var product in products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            simulated[product.Id] = SavedProductReader.Build(product, pricedDetails[product.Id], basis, repriced);
        }

        return new SimulatedPrices(simulated, IngredientCostRules.ReachedBy(materials.Values, bomLines, [.. newCosts.Keys]));
    }
}

/// <summary>
/// What <see cref="PriceChangeSimulation.Run"/> works out: each product priced with the changes, by id, and each changed
/// material's reach, by id: itself and every made and linked ingredient depending on it, however deeply.
/// </summary>
public sealed record SimulatedPrices(IReadOnlyDictionary<int, SavedProduct> Products, IReadOnlyDictionary<int, IReadOnlySet<int>> ReachedBy);
