using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UseCases.Reports;

/// <summary>What the Reports screen reads: what its tables are worked out from, and the product versions Margin Erosion compares with.</summary>
public interface IReportQueries
{
    /// <summary>
    /// Every product as it's saved with what prices it (SavedProductReader), every raw material, every price history entry
    /// and every made ingredient's BOM line, read side by side, off the calling thread.
    /// </summary>
    Task<ReportsRead> ReadAsync(CancellationToken cancellationToken);

    /// <summary>Each product's latest version saved by <paramref name="asOf"/>; a product with none then has none here.</summary>
    Task<IReadOnlyList<ProductVersion>> ReadVersionsAsOfAsync(DateTime asOf, CancellationToken cancellationToken);
}

/// <summary>What a visit to Reports reads: the products with their details (by product id), the materials, their price history and the BOM lines.</summary>
public sealed record ReportsRead(
    IReadOnlyList<Product> Products,
    IReadOnlyDictionary<int, ProductDetail> Details,
    IReadOnlyList<RawMaterial> Materials,
    IReadOnlyList<RawMaterialPriceHistory> PriceHistory,
    IReadOnlyList<IngredientBomLine> BomLines);

/// <inheritdoc cref="IReportQueries"/>
public sealed class ReportQueries(
    IProductRepository productRepository,
    SavedProductReader savedProducts,
    IRawMaterialRepository rawMaterialRepository,
    IRawMaterialPriceHistoryRepository priceHistoryRepository,
    IIngredientBomLineRepository bomLineRepository,
    IProductVersionRepository versionRepository) : IReportQueries
{
    // Off the calling thread, as the screens read their details: the products and their details in the same dozen or so
    // queries however many there are.
    public Task<ReportsRead> ReadAsync(CancellationToken cancellationToken) => Task.Run(
        async () =>
        {
            var productsRead = ReadProductsAsync();
            var materialsRead = rawMaterialRepository.GetAllAsync(cancellationToken);
            var historyRead = priceHistoryRepository.GetAllAsync(cancellationToken);
            var bomLinesRead = bomLineRepository.GetAllAsync(cancellationToken);
            await Task.WhenAll(productsRead, materialsRead, historyRead, bomLinesRead);

            var (products, details) = await productsRead;
            return new ReportsRead(products, details, await materialsRead, await historyRead, await bomLinesRead);

            async Task<(IReadOnlyList<Product> Products, IReadOnlyDictionary<int, ProductDetail> Details)> ReadProductsAsync()
            {
                var products = await productRepository.GetAllAsync(cancellationToken);
                return (products, await savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], cancellationToken));
            }
        },
        cancellationToken);

    public Task<IReadOnlyList<ProductVersion>> ReadVersionsAsOfAsync(DateTime asOf, CancellationToken cancellationToken) =>
        Task.Run(() => versionRepository.GetLatestAsOfAsync(asOf, cancellationToken), cancellationToken);
}
