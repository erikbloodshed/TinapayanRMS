using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A row of Reports' Cost Drivers (2026-10-29): a purchased material, and what it makes up of the products in Production,
/// one sellable unit of each.
/// </summary>
/// <param name="CostInProducts">What one of each product using it spends on it, added up: its share of their material costs, in pesos.</param>
/// <param name="ShareOfAll">That, of all the products' material costs, one of each, in percent.</param>
/// <param name="CumulativeShare">This row's share and every row's above it: where the materials that matter most stop.</param>
/// <param name="LargestShare">The most it makes up of one product's material cost, in percent, and which product.</param>
public sealed record CostDriverRow(
    int RawMaterialId,
    string Name,
    string Category,
    int Products,
    decimal CostInProducts,
    decimal ShareOfAll,
    decimal CumulativeShare,
    decimal LargestShare,
    string LargestIn);

/// <summary>
/// Works out Cost Drivers: for each purchased material, every product in Production priced with it at no cost against as
/// it is, at buffered costs, the way the Price Simulator prices a change (IngredientCostRules.Reprice, SavedProductReader.Build),
/// so it counts however the material gets in. Only the products it reaches are priced again (IngredientCostRules.ReachedBy,
/// PriceSimulatorReport.MaterialIdsOf), which keeps it to a few thousand pricings for a shop's worth of products.
/// </summary>
public static class CostDriverAnalysis
{
    /// <param name="products">Every product as saved; those in Production that aren't deleted are counted.</param>
    /// <param name="details">Their details, and those of every product a composite among them holds.</param>
    public static IReadOnlyList<CostDriverRow> Analyse(
        IReadOnlyList<Product> products,
        IReadOnlyDictionary<int, ProductDetail> details,
        IReadOnlyDictionary<int, RawMaterial> materials,
        IReadOnlyCollection<IngredientBomLine> bomLines)
    {
        List<Product> counted = [];
        Dictionary<int, decimal> materialCostNow = [];
        foreach (var product in products)
        {
            if (product.Stage == ProductStage.Production && !product.IsDeleted && details.TryGetValue(product.Id, out var detail))
            {
                counted.Add(product);
                materialCostNow[product.Id] = SavedProductReader.Build(product, detail).Costs.Material;
            }
        }

        var allMaterialCost = materialCostNow.Values.Sum();
        if (allMaterialCost <= 0m)
        {
            return [];
        }

        var materialsOf = counted.ToDictionary(p => p.Id, p => PriceSimulatorReport.MaterialIdsOf(p.Id, details));
        List<(RawMaterial Material, int Products, decimal Cost, decimal LargestShare, string LargestIn)> found = [];
        foreach (var material in materials.Values.Where(m => m.Source == RawMaterialSource.Purchased))
        {
            var reached = IngredientCostRules.ReachedBy(materials.Values, bomLines, material.Id);
            List<Product> using_ = [.. counted.Where(p => materialsOf[p.Id].Overlaps(reached))];
            if (using_.Count == 0)
            {
                continue;
            }

            var withoutIt = IngredientCostRules.Reprice(materials, bomLines, new Dictionary<int, (decimal, decimal)> { [material.Id] = (0m, 0m) });
            var pricedWithoutIt = using_.Any(p => p.IsComposite) ? SavedProductReader.PricedAt(details, withoutIt) : details;
            decimal cost = 0m, largestShare = 0m;
            var largestIn = string.Empty;
            var productsUsing = 0;
            foreach (var product in using_)
            {
                var spent = materialCostNow[product.Id] - SavedProductReader.Build(product, pricedWithoutIt[product.Id], CostBasis.Buffered, withoutIt).Costs.Material;
                if (spent <= 0m)
                {
                    continue;
                }

                productsUsing++;
                cost += spent;
                var share = spent / materialCostNow[product.Id] * 100m;
                if (share > largestShare)
                {
                    (largestShare, largestIn) = (share, product.Name);
                }
            }

            if (productsUsing > 0)
            {
                found.Add((material, productsUsing, cost, largestShare, largestIn));
            }
        }

        var cumulative = 0m;
        List<CostDriverRow> rows = [];
        foreach (var (material, count, cost, largestShare, largestIn) in found.OrderByDescending(f => f.Cost))
        {
            var share = cost / allMaterialCost * 100m;
            cumulative += share;
            rows.Add(new CostDriverRow(material.Id, material.Name, material.Category?.Name ?? string.Empty, count, cost, share, cumulative, largestShare, largestIn));
        }

        return rows;
    }
}
