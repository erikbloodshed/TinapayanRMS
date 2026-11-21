using AwesomeAssertions;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Reports;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Reports;

/// <summary>What Reports reads: every product with what prices it, every material, and no version saved before the first.</summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class ReportQueriesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task Every_product_comes_with_its_details_and_every_material_with_it()
    {
        var flourId = await AddIngredientAsync("Bread Flour", costPerGram: 0.05m);
        var doughRoleId = await AddRoleAsync("Dough");
        var (productId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 100m)]);

        var read = await CreateQueries().ReadAsync(Cancellation);

        read.Products.Select(p => p.Id).Should().Equal(productId);
        read.Details.Keys.Should().Equal(productId);
        read.Materials.Select(m => m.Id).Should().Contain(flourId);
    }

    [Test]
    public async Task Before_any_version_is_saved_there_is_none_to_compare_with()
    {
        await AddProductAsync("Pandesal");

        (await CreateQueries().ReadVersionsAsOfAsync(new DateTime(2026, 11, 21, 23, 59, 59), Cancellation)).Should().BeEmpty();
    }

    private ReportQueries CreateQueries()
    {
        var factory = CreateDbContextFactory();
        return new ReportQueries(
            new ProductRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                new ProductRepository(factory),
                new CostingDefaultsRepository(factory),
                CreateUnitOfWork()),
            new RawMaterialRepository(factory),
            new RawMaterialPriceHistoryRepository(factory),
            new IngredientBomLineRepository(factory),
            new ProductVersionRepository(factory));
    }
}
