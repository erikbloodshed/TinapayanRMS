using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>Several products' Current Price set at once (IProductRepository.SetCurrentPricesAsync), as the Price Simulator sets them.</summary>
[Collection(DatabaseCollection.Name)]
public class ProductCurrentPricePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateRepository() => new(CreateDbContextFactory());

    private Task<int> SaveNewAsync(string name, decimal? currentPrice) =>
        CreateRepository().SaveAsync(new Product { Name = name, CurrentPrice = currentPrice }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    [Fact]
    public async Task Each_price_is_set_and_stamped_and_nothing_else_changes()
    {
        var pandesalId = await SaveNewAsync("Pandesal", 3m);
        var ensaymadaId = await SaveNewAsync("Ensaymada", null);
        var untouchedId = await SaveNewAsync("Cheese Roll", 38m);

        await CreateRepository().SetCurrentPricesAsync([(pandesalId, 3.5m), (ensaymadaId, 20m)], Cancellation);

        var products = (await CreateRepository().GetAllAsync(Cancellation)).ToDictionary(p => p.Id);
        products[pandesalId].CurrentPrice.Should().Be(3.5m);
        products[ensaymadaId].CurrentPrice.Should().Be(20m);
        products[untouchedId].CurrentPrice.Should().Be(38m);
        products[pandesalId].UpdatedAt.Should().NotBeNull();
        products[pandesalId].Name.Should().Be("Pandesal");
    }

    [Fact]
    public async Task A_negative_price_or_a_missing_product_changes_nothing()
    {
        var pandesalId = await SaveNewAsync("Pandesal", 3m);

        var negative = () => CreateRepository().SetCurrentPricesAsync([(pandesalId, 4m), (pandesalId, -1m)], Cancellation);
        await negative.Should().ThrowAsync<BusinessRuleException>();
        var missing = () => CreateRepository().SetCurrentPricesAsync([(pandesalId, 4m), (pandesalId + 1000, 5m)], Cancellation);
        await missing.Should().ThrowAsync<BusinessRuleException>();

        (await CreateRepository().GetByIdAsync(pandesalId, Cancellation))!.CurrentPrice.Should().Be(3m);
    }
}
