using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.UseCases.PriceSimulator;

/// <summary>What the Price Simulator reads: everything its figures are worked out from, and the scenarios saved.</summary>
public interface IPriceSimulatorQueries
{
    /// <summary>
    /// Every product as it's saved with what prices it (SavedProductReader), every raw material, every made ingredient's BOM
    /// line, every brand price and the scenarios saved, read side by side, off the calling thread.
    /// </summary>
    Task<PriceSimulatorRead> ReadAsync(CancellationToken cancellationToken);

    /// <summary>The scenarios saved, by name.</summary>
    Task<IReadOnlyList<PriceScenario>> ReadScenariosAsync(CancellationToken cancellationToken);
}

/// <summary>What a load of the Price Simulator reads: the products with their details (by product id), the materials, the BOM lines, the brand prices and the scenarios.</summary>
public sealed record PriceSimulatorRead(
    IReadOnlyList<Product> Products,
    IReadOnlyDictionary<int, ProductDetail> Details,
    IReadOnlyList<RawMaterial> Materials,
    IReadOnlyList<IngredientBomLine> BomLines,
    IReadOnlyList<RawMaterialBrandPrice> BrandPrices,
    IReadOnlyList<PriceScenario> Scenarios);

/// <inheritdoc cref="IPriceSimulatorQueries"/>
public sealed class PriceSimulatorQueries(
    IProductRepository productRepository,
    SavedProductReader savedProducts,
    IRawMaterialRepository rawMaterialRepository,
    IIngredientBomLineRepository bomLineRepository,
    IRawMaterialBrandPriceRepository brandPriceRepository,
    IPriceScenarioRepository scenarioRepository) : IPriceSimulatorQueries
{
    public Task<PriceSimulatorRead> ReadAsync(CancellationToken cancellationToken) => Task.Run(
        async () =>
        {
            var productsRead = productRepository.GetAllAsync(cancellationToken);
            var materialsRead = rawMaterialRepository.GetAllAsync(cancellationToken);
            var bomLinesRead = bomLineRepository.GetAllAsync(cancellationToken);
            var pricesRead = brandPriceRepository.GetAllAsync(cancellationToken);
            var scenariosRead = scenarioRepository.GetAllAsync(cancellationToken);
            await Task.WhenAll(productsRead, materialsRead, bomLinesRead, pricesRead, scenariosRead);

            var products = await productsRead;
            var details = await savedProducts.ReadDetailsAsync([.. products.Select(p => p.Id)], cancellationToken);
            return new PriceSimulatorRead(products, details, await materialsRead, await bomLinesRead, await pricesRead, await scenariosRead);
        },
        cancellationToken);

    public Task<IReadOnlyList<PriceScenario>> ReadScenariosAsync(CancellationToken cancellationToken) => scenarioRepository.GetAllAsync(cancellationToken);
}
