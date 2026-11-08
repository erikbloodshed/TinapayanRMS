using System.ComponentModel;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Mcp.Results;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Mcp.Tools;

/// <summary>
/// Products' costs as the app works them out, which the database doesn't hold: a product's cost and price are computed on
/// read (SavedProductReader), so SQL alone can't show them. Prices the way Reports and the Products screen do, and
/// simulates price changes the way the Price Simulator screen does, with the same code. Nothing is saved.
/// </summary>
[McpServerToolType]
public sealed class CostingTools(
    IProductRepository productRepository,
    IRawMaterialRepository rawMaterialRepository,
    IIngredientBomLineRepository bomLineRepository,
    IRawMaterialBrandPriceRepository brandPriceRepository,
    SavedProductReader savedProducts)
{
    private const string UnbufferedDescription =
        "Price at the materials' true costs instead of their buffered ones (the Products screen's Unbuffered switch). Buffered, as the app prices by default, when left out.";

    [McpServerTool(Name = "list_products", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "Lists the bakeshop's products (not deleted ones), each priced as Reports prices it: base cost, suggested selling price, " +
        "current price and the markup it earns. Use it to find a product's exact name, or to compare products.")]
    public Task<IReadOnlyList<ProductListing>> ListProductsAsync(
        [Description("Only products whose name contains this, ignoring case. Every product when left out.")] string? search = null,
        [Description(UnbufferedDescription)] bool unbuffered = false,
        CancellationToken cancellationToken = default) =>
        RefusingRulesAsync<IReadOnlyList<ProductListing>>(async () =>
        {
            var products = await productRepository.GetAllAsync(cancellationToken);
            List<Product> matching = [.. products.Where(p => search is null || p.Name.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase))];
            var priced = await savedProducts.ReadAllAsync(matching, BasisOf(unbuffered), cancellationToken);
            return [.. priced.Select(ProductListing.From)];
        });

    [McpServerTool(Name = "get_product_costing", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "One product's full costing, as the Products screen shows it once opened: each recipe section's batch, lines and cost per " +
        "piece, a composite's products, its packaging lines, and the Costing tab from material cost through wastage, labor, " +
        "overhead, administrative and markup to the selling price with VAT.")]
    public Task<ProductCostSheet> GetProductCostingAsync(
        [Description("The product's name, or a part of it that names only it.")] string product,
        [Description(UnbufferedDescription)] bool unbuffered = false,
        CancellationToken cancellationToken = default) =>
        RefusingRulesAsync(async () =>
        {
            var found = Named(await productRepository.GetAllAsync(cancellationToken), p => p.Name, product, "product");
            return ProductCostSheet.From(await savedProducts.ReadAsync(found, BasisOf(unbuffered), cancellationToken));
        });

    [McpServerTool(Name = "simulate_price_changes", ReadOnly = true, Idempotent = true, OpenWorld = false)]
    [Description(
        "What-if: tries new prices for purchased raw materials and shows every product they reach, with its base cost, suggested " +
        "price and markup now and with the changes, as the Price Simulator screen does. A change reaches made and linked " +
        "ingredients and the composites holding a product as a real price change would. Nothing is saved.")]
    public Task<PriceSimulation> SimulatePriceChangesAsync(
        [Description("The price changes to try together, one per material.")] IReadOnlyList<PriceChangeInput> changes,
        [Description(UnbufferedDescription)] bool unbuffered = false,
        [Description("List every product, not only the ones the changes reach.")] bool includeUnaffected = false,
        CancellationToken cancellationToken = default) =>
        RefusingRulesAsync(async () =>
        {
            if (changes.Count == 0)
            {
                throw new McpException("Give at least one price change.");
            }

            // What the Price Simulator screen reads (PriceSimulatorViewModel.ReadAsync), one read after another.
            var products = await productRepository.GetAllAsync(cancellationToken);
            var materials = (await rawMaterialRepository.GetAllAsync(cancellationToken)).ToDictionary(m => m.Id);
            var bomLines = await bomLineRepository.GetAllAsync(cancellationToken);
            var prices = (await brandPriceRepository.GetAllAsync(cancellationToken)).ToLookup(p => p.RawMaterialId);
            var details = await savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], cancellationToken);

            // Each change worked out and checked as the Price Simulator works out one typed into its Price Changes table.
            List<TriedPriceChange> rows = [.. changes.Select(change => TriedFor(change, materials, prices))];
            if (rows.GroupBy(r => r.Material.Id).FirstOrDefault(g => g.Count() > 1) is { } repeated)
            {
                throw new McpException($"{repeated.First().Material.Name} is changed more than once. Give it one change.");
            }

            List<(int MaterialId, decimal Cost, decimal BufferedCost)> newCosts = [.. rows.Select(r => (r.Material.Id, r.Cost, r.BufferedCost))];

            // As PriceSimulatorViewModel.RecalculateAsync works them out.
            var basis = BasisOf(unbuffered);
            var repriced = IngredientCostRules.Reprice(materials, bomLines, newCosts.ToDictionary(c => c.MaterialId, c => (c.Cost, c.BufferedCost)));
            var pricedDetails = SavedProductReader.PricedAt(details, repriced);
            List<PriceSimulatorProductRow> productRows =
            [
                .. products.Select(p => new PriceSimulatorProductRow(
                    p,
                    SavedProductReader.Build(p, details[p.Id], basis).Costs,
                    SavedProductReader.Build(p, pricedDetails[p.Id], basis, repriced).Costs)),
            ];

            var reachedBy = IngredientCostRules.ReachedBy(materials.Values, bomLines, [.. rows.Select(r => r.Material.Id)]);
            List<PriceChangeTried> tried =
            [
                .. rows.Select(r => new PriceChangeTried(
                    r.Material.Name,
                    r.Unit.Name,
                    r.Unit.Price,
                    r.NewPrice,
                    r.ChangePercent,
                    [
                        .. reachedBy[r.Material.Id]
                            .Where(id => id != r.Material.Id)
                            .Select(id => materials[id].Name)
                            .Order(StringComparer.OrdinalIgnoreCase),
                    ])),
            ];

            return new PriceSimulation(
                basis.ToString(),
                tried,
                productRows.Count(r => r.IsAffected),
                [
                    .. productRows
                        .Where(r => includeUnaffected || r.IsAffected)
                        .OrderByDescending(r => r.ChangePercent)
                        .ThenBy(r => r.ProductName, StringComparer.OrdinalIgnoreCase)
                        .Select(SimulatedProduct.From),
                ]);
        });

    /// <summary>
    /// <paramref name="change"/> worked out as the Price Simulator works out one typed into its Price Changes table, and
    /// refused for what that refuses: a fall of 100% or more, a price of 0, a percentage on a material with no price.
    /// </summary>
    private static TriedPriceChange TriedFor(
        PriceChangeInput change, IReadOnlyDictionary<int, RawMaterial> materials, ILookup<int, RawMaterialBrandPrice> prices)
    {
        var material = Named(materials.Values, m => m.Name, change.Material, "raw material");
        if (material.Source != RawMaterialSource.Purchased)
        {
            throw new McpException(
                $"{material.Name} is {(material.Source == RawMaterialSource.Made ? "made in-house" : "linked to another ingredient")}: its cost " +
                "follows from what it's made of or cut from. Change the price of a purchased material instead.");
        }

        if (change.NewPrice is null == change.ChangePercent is null)
        {
            throw new McpException($"{material.Name}: give either its new price or its change in percent, not both or neither.");
        }

        // Either the percentage or the price was given, so there's a price to try (or a refusal) either way.
        return TriedPriceChange.Of(
            material, PurchaseUnit.Of(material, [.. prices[material.Id]]), change.ChangePercent, change.NewPrice, percentTyped: change.ChangePercent is not null)!;
    }

    /// <summary>The one item named <paramref name="name"/>, ignoring case, or else the one whose name contains it.</summary>
    private static T Named<T>(IEnumerable<T> items, Func<T, string> nameOf, string name, string kind)
    {
        var search = name.Trim();
        List<T> candidates = [.. items];
        if (candidates.Where(i => string.Equals(nameOf(i), search, StringComparison.OrdinalIgnoreCase)).ToList() is [var exact])
        {
            return exact;
        }

        return candidates.Where(i => nameOf(i).Contains(search, StringComparison.OrdinalIgnoreCase)).ToList() switch
        {
            [var only] => only,
            [] => throw new McpException($"No {kind} is named \"{search}\" or has it in its name."),
            var several => throw new McpException(
                $"\"{search}\" is in the names of {several.Count} {kind}s: {string.Join(", ", several.Take(20).Select(nameOf))}" +
                $"{(several.Count > 20 ? ", …" : string.Empty)}. Give the full name."),
        };
    }

    private static CostBasis BasisOf(bool unbuffered) => unbuffered ? CostBasis.Unbuffered : CostBasis.Buffered;

    /// <summary>A business rule's refusal as a tool error the model reads; any other failure stays the SDK's generic one.</summary>
    private static async Task<T> RefusingRulesAsync<T>(Func<Task<T>> run)
    {
        try
        {
            return await run();
        }
        catch (BusinessRuleException ex)
        {
            throw new McpException(ex.Message, ex);
        }
    }
}
