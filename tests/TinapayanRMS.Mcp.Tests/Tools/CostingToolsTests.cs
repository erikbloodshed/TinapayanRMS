using System.Text.Json;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Mcp.Results;
using TinapayanRMS.Mcp.Serialization;
using TinapayanRMS.Mcp.Tests.Fixtures;
using TinapayanRMS.Mcp.Tools;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Mcp.Tests.Tools;

/// <summary>
/// The MCP server's tools against a real database, read through a read-only connection as the server reads: a product is
/// costed as the Products screen and Reports cost it, a price change reaches what the Price Simulator screen's would, and
/// what the screens refuse is refused with a message the model can act on.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class CostingToolsTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>
    /// Egg at 0.014 a gram (priced per gram), egg white linked to it (70% of its weight, 30% of its cost, so 0.006 a gram),
    /// flour at 0.05 with no price, and custard made from 100 g of egg. Meringue is 50 g a piece of an egg white dough
    /// (0.30), Pandesal 50 g a piece of a flour dough (2.50), and the Party Box holds two Meringues and a Pandesal (3.10).
    /// No wastage and no costing, so each base cost is its materials.
    /// </summary>
    private async Task<Bakery> AddBakeryAsync()
    {
        await WithoutWastageAsync();
        var doughId = await AddRoleAsync("Dough");
        var eggId = await AddIngredientAsync("Egg", 0.014m);
        await SetPriceAsync(eggId, null, 0.014m);
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        int whiteId;
        await using (var dbContext = CreateDbContext())
        {
            var whiteCost = RawMaterialRules.StoredCost(RawMaterialRules.LinkedCost(0.014m, 70m, 30m));
            var white = new RawMaterial
            {
                Name = "Egg White", CategoryId = dbContext.RawMaterials.Single(m => m.Id == eggId).CategoryId, Source = RawMaterialSource.Linked,
                LinkedToId = eggId, WeightSharePercent = 70m, CostSharePercent = 30m, CostPerBaseUnit = whiteCost, BufferedCostPerBaseUnit = whiteCost,
            };
            dbContext.RawMaterials.Add(white);
            await dbContext.SaveChangesAsync(Cancellation);
            whiteId = white.Id;
        }

        await AddMadeIngredientAsync("Custard", [(eggId, 100m)]);
        var (meringueId, _) = await AddProductWithSubAssemblyAsync("Meringue", doughId, [(whiteId, 1000m)], weightPerPiece: 50m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughId, [(flourId, 1000m)], weightPerPiece: 50m);
        var boxId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Party Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(meringueId, 2), (pandesalId, 1)] },
            null,
            null,
            null,
            Cancellation);
        return new Bakery(meringueId, pandesalId, boxId);
    }

    private sealed record Bakery(int MeringueId, int PandesalId, int BoxId);

    [Test]
    public async Task A_products_costing_is_what_the_Products_screen_reads()
    {
        var bakery = await AddBakeryAsync();

        var meringue = await CreateTools().GetProductCostingAsync("meringue", cancellationToken: Cancellation);
        var box = await CreateTools().GetProductCostingAsync("Party Box", cancellationToken: Cancellation);

        var savedMeringue = await ReadAsync(bakery.MeringueId);
        meringue.Name.Should().Be("Meringue");
        meringue.CostBasis.Should().Be("Buffered");
        meringue.Costs.Should().Be(CostSummary.From(savedMeringue.Costs));
        meringue.Costs.BaseCost.Should().Be(0.30m);
        var section = meringue.Sections.Should().ContainSingle().Subject;
        section.Name.Should().Be("Dough");
        section.WeightPerPieceGrams.Should().Be(50m);
        section.CostPerPiece.Should().Be(0.30m);
        section.Lines.Should().Equal(new LineCosting("Egg White", 1000m, 0.006m, 6m));

        box.Costs.Should().Be(CostSummary.From((await ReadAsync(bakery.BoxId)).Costs));
        box.Costs.BaseCost.Should().Be(3.10m);
        box.Components.Should().Equal(
            new ComponentCosting("Meringue", 2, 0.30m, 0.60m, null),
            new ComponentCosting("Pandesal", 1, 2.50m, 2.50m, null));
    }

    [Test]
    public async Task Listing_products_finds_them_by_part_of_their_name_and_prices_each()
    {
        await AddBakeryAsync();

        var listed = await CreateTools().ListProductsAsync("PA", cancellationToken: Cancellation);

        listed.Select(p => (p.Name, p.BaseCost)).Should().Equal(("Pandesal", 2.50m), ("Party Box", 3.10m));
        (await CreateTools().ListProductsAsync(cancellationToken: Cancellation)).Should().HaveCount(3);
    }

    [Test]
    public async Task A_name_that_names_no_product_or_several_is_refused_with_what_it_could_mean()
    {
        await AddBakeryAsync();

        await FluentActions.Awaiting(() => CreateTools().GetProductCostingAsync("Ensaymada", cancellationToken: Cancellation))
            .Should().ThrowAsync<McpException>().WithMessage("No product is named \"Ensaymada\"*");
        await FluentActions.Awaiting(() => CreateTools().GetProductCostingAsync("e", cancellationToken: Cancellation))
            .Should().ThrowAsync<McpException>().WithMessage("\"e\" is in the names of 2 products: Meringue, Pandesal. Give the full name.");
    }

    [Test]
    public async Task A_rise_in_egg_reaches_the_linked_egg_white_its_product_and_the_composite_holding_it()
    {
        await AddBakeryAsync();

        var simulation = await CreateTools().SimulatePriceChangesAsync([new PriceChangeInput("Egg", ChangePercent: 10m)], cancellationToken: Cancellation);

        simulation.Changes.Should().BeEquivalentTo(
            [new PriceChangeTried("Egg", "gram", 0.014m, 0.0154m, 10m, ["Custard", "Egg White"])], options => options.WithStrictOrdering());
        simulation.ProductsReached.Should().Be(2);
        simulation.Products.Select(p => p.Name).Should().Equal("Meringue", "Party Box");
        var meringue = simulation.Products[0];
        meringue.BaseCost.Should().Be(0.30m);
        meringue.SimulatedBaseCost.Should().BeApproximately(0.33m, 0.0000001m);
        meringue.ChangePercent.Should().BeApproximately(10m, 0.00001m);
        simulation.Products[1].SimulatedBaseCost.Should().BeApproximately(3.16m, 0.0000001m, "the box holds two meringues");
    }

    [Test]
    public async Task Products_a_change_doesnt_reach_are_listed_only_when_asked_for_and_unchanged()
    {
        await AddBakeryAsync();

        var simulation = await CreateTools().SimulatePriceChangesAsync(
            [new PriceChangeInput("Egg", NewPrice: 0.028m)], includeUnaffected: true, cancellationToken: Cancellation);

        simulation.ProductsReached.Should().Be(2);
        var pandesal = simulation.Products.Should().ContainSingle(p => p.Name == "Pandesal").Subject;
        pandesal.SimulatedBaseCost.Should().Be(pandesal.BaseCost);
        simulation.Products[^1].Should().Be(pandesal, "the most moved come first");
    }

    [Test]
    public async Task A_material_with_no_price_yet_takes_a_new_price_as_its_first()
    {
        await AddBakeryAsync();

        var simulation = await CreateTools().SimulatePriceChangesAsync([new PriceChangeInput("Flour", NewPrice: 0.06m)], cancellationToken: Cancellation);

        simulation.Changes.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Unit = "gram", CurrentPrice = (decimal?)null, NewPrice = 0.06m });
        simulation.Products.Select(p => (p.Name, p.SimulatedBaseCost)).Should().Equal(("Pandesal", 3.00m), ("Party Box", 3.60m));
    }

    public static IEnumerable<Func<(PriceChangeInput[], string)>> RefusedChanges() =>
    [
        () => ([new("Custard", NewPrice: 1m)], "Custard is made in-house*"),
        () => ([new("Egg White", NewPrice: 1m)], "Egg White is linked to another ingredient*"),
        () => ([new("Egg", NewPrice: 1m, ChangePercent: 5m)], "Egg: give either its new price or its change in percent, not both or neither."),
        () => ([new("Egg")], "Egg: give either its new price or its change in percent, not both or neither."),
        () => ([new("Egg", ChangePercent: 5m), new("egg", NewPrice: 1m)], "Egg is changed more than once. Give it one change."),
        () => ([new("Flour", ChangePercent: 5m)], $"Flour: {PriceChangeRules.NoPriceToChangeText}"),
        () => ([new("Egg", ChangePercent: -100m)], PriceChangeRules.FallTooFarText),
        () => ([new("Egg", NewPrice: 0m)], PriceChangeRules.NewPriceTooLowText),
        () => ([], "Give at least one price change."),
    ];

    [Test]
    [MethodDataSource(nameof(RefusedChanges))]
    public async Task What_the_Price_Simulator_screen_refuses_is_refused_with_its_reason(PriceChangeInput[] changes, string message)
    {
        await AddBakeryAsync();

        await FluentActions.Awaiting(() => CreateTools().SimulatePriceChangesAsync(changes, cancellationToken: Cancellation))
            .Should().ThrowAsync<McpException>().WithMessage(message);
    }

    [Test]
    public async Task The_servers_connection_reads_but_refuses_every_write()
    {
        await AddBakeryAsync();
        await using var dbContext = new TinapayanRMSDbContext(TinapayanRMSDbContext.CreateOptions(ReadOnlyConnection.Of(fixture.ConnectionString)));

        (await dbContext.Products.CountAsync(Cancellation)).Should().Be(3);
        dbContext.Products.Add(new Product { Name = "Ensaymada" });
        var refused = await FluentActions.Awaiting(() => dbContext.SaveChangesAsync(Cancellation)).Should().ThrowAsync<DbUpdateException>();
        refused.WithInnerException<PostgresException>().Which.SqlState.Should().Be(PostgresErrorCodes.ReadOnlySqlTransaction);
    }

    [Test]
    public void Decimals_are_written_to_six_places()
    {
        var listing = new ProductListing("Pandesal", "Breads", "Production", false, 1, 3.3271741114058355437703554377m, 5.96m, 6m, 5m, null, true);

        var json = JsonSerializer.Serialize(listing, ToolJson.Options);

        json.Should().Contain("\"baseCost\":3.327174,");
    }

    /// <summary>The tools as the server makes them, on a read-only connection to the test database (ReadOnlyConnection).</summary>
    private CostingTools CreateTools()
    {
        var factory = new SharedTransactionDbContextFactory(ReadOnlyConnection.Of(fixture.ConnectionString));
        var productRepository = new ProductRepository(factory);
        return new CostingTools(
            productRepository,
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                productRepository,
                new CostingDefaultsRepository(factory),
                CreateUnitOfWork()));
    }

    private async Task<SavedProduct> ReadAsync(int productId)
    {
        var factory = CreateDbContextFactory();
        var productRepository = new ProductRepository(factory);
        var reader = new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            productRepository,
            new CostingDefaultsRepository(factory),
            CreateUnitOfWork());
        return await reader.ReadAsync((await productRepository.GetByIdAsync(productId, Cancellation))!, Cancellation);
    }
}
