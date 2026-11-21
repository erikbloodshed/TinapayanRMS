using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.PriceSimulator;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.PriceSimulator;

/// <summary>
/// What the Price Simulator reads (every product with what prices it, the materials and their brand prices) and what it
/// does to a scenario: saved under a name, replacing one of that name's, read back, and deleted. No real price changes.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class PriceSimulatorUseCasesTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public async Task A_load_reads_every_product_with_its_details_and_every_material()
    {
        var flourId = await AddIngredientAsync("Bread Flour", costPerGram: 0.05m);
        var doughRoleId = await AddRoleAsync("Dough");
        var (productId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 100m)]);

        var read = await CreateQueries().ReadAsync(Cancellation);

        read.Products.Select(p => p.Id).Should().Equal(productId);
        read.Details.Keys.Should().Equal(productId);
        read.Materials.Select(m => m.Id).Should().Contain(flourId);
        read.Scenarios.Should().BeEmpty();
    }

    [Test]
    public async Task A_scenario_saved_under_a_name_replaces_that_names_and_a_delete_removes_it()
    {
        var flourId = await AddIngredientAsync("Bread Flour", costPerGram: 0.05m);
        var editor = new PriceScenarioEditor(new PriceScenarioRepository(CreateDbContextFactory()));
        var savedAt = new DateTime(2026, 11, 21, 9, 0, 0);

        var id = await editor.SaveAsync("Flour up", [new PriceScenarioLine { RawMaterialId = flourId, ChangePercent = 10m }], savedAt, Cancellation);
        var again = await editor.SaveAsync("Flour up", [new PriceScenarioLine { RawMaterialId = flourId, ChangePercent = 20m }], savedAt, Cancellation);

        var scenarios = await CreateQueries().ReadScenariosAsync(Cancellation);
        scenarios.Should().ContainSingle().Which.Lines.Should().ContainSingle().Which.ChangePercent.Should().Be(20m);
        again.Should().Be(id, "a scenario of that name is replaced, not added beside it");

        await editor.DeleteAsync(id, Cancellation);
        (await CreateQueries().ReadScenariosAsync(Cancellation)).Should().BeEmpty();
    }

    private PriceSimulatorQueries CreateQueries()
    {
        var factory = CreateDbContextFactory();
        return new PriceSimulatorQueries(
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
            new IngredientBomLineRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new PriceScenarioRepository(factory));
    }
}
