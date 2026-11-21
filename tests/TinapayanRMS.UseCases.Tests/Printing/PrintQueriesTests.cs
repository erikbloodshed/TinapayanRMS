using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Printing;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Printing;

/// <summary>What the Print dialog reads: its lists as saved now, and a set's products priced as saved, in the order asked.</summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class PrintQueriesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task The_lists_hold_every_product_in_use_folder_and_category_as_saved()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var deletedId = await AddProductAsync("Old Monay");
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(deletedId, Cancellation);
        await new ProductCategoryRepository(CreateDbContextFactory()).AddAsync(new ProductCategory { Name = "Breads" }, Cancellation);
        await new ProductFolderRepository(CreateDbContextFactory()).AddAsync(new ProductFolder { Name = "Christmas", Stage = ProductStage.Production }, Cancellation);

        var lists = await CreateQueries().ReadListsAsync(Cancellation);

        lists.Products.Select(p => p.Id).Should().BeEquivalentTo([pandesalId, ensaymadaId], "a deleted product isn't printed");
        lists.Categories.Select(c => c.Name).Should().Contain("Breads");
        lists.Folders.Should().ContainSingle(f => f.Name == "Christmas" && f.Stage == ProductStage.Production);
    }

    [Test]
    public async Task A_read_its_token_cut_short_ends_as_a_cancellation()
    {
        await AddProductAsync("Pandesal");
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await CreateQueries().Awaiting(q => q.ReadListsAsync(cancelled.Token)).Should().ThrowAsync<OperationCanceledException>();
    }

    [Test]
    public async Task A_set_is_priced_as_saved_in_the_order_asked()
    {
        var flourId = await AddIngredientAsync("Bread Flour", costPerGram: 0.05m);
        var doughRoleId = await AddRoleAsync("Dough");
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 100m)]);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 200m)]);
        var queries = CreateQueries();
        var products = (await queries.ReadListsAsync(Cancellation)).Products;

        var priced = await queries.PriceAsSavedAsync(
            [products.Single(p => p.Id == ensaymadaId), products.Single(p => p.Id == pandesalId)], CostBasis.Buffered, Cancellation);

        priced.Select(s => s.Product.Id).Should().Equal(ensaymadaId, pandesalId);
        priced.Should().OnlyContain(s => s.Costs.BaseCost > 0m);
    }

    private PrintQueries CreateQueries()
    {
        var factory = CreateDbContextFactory();
        return new PrintQueries(
            new ProductRepository(factory),
            new ProductFolderRepository(factory),
            new ProductCategoryRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                new ProductRepository(factory),
                new CostingDefaultsRepository(factory),
                CreateUnitOfWork()));
    }
}
