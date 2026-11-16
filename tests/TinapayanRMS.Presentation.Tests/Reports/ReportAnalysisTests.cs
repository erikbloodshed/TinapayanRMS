using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing.Versions;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Reports;

namespace TinapayanRMS.Presentation.Tests.Reports;

/// <summary>Reports' Cost Drivers and Margin Erosion, worked out from products and versions as saved.</summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class ReportAnalysisTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task Cost_drivers_rank_the_purchased_materials_by_what_the_products_in_Production_spend_on_them() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var sugarId = await AddIngredientAsync("Sugar", costPerGram: 0.09m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(sugarId, 1000m)]);
        var (ensaymadaId, _) = await AddProductWithSubAssemblyAsync("Ensaymada", doughRoleId, [(flourId, 900m), (syrupId, 100m)], weightPerPiece: 10m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)], weightPerPiece: 10m);
        await SetStageAsync([ensaymadaId, pandesalId], ProductStage.Production);

        var rows = await AnalyseCostDriversAsync();

        // A piece of each: flour 0.45 and 0.50, sugar (through the syrup) 0.09, of 1.04 in all.
        rows.Select(r => (r.Name, r.Products)).Should().Equal(("Flour", 2), ("Sugar", 1));
        rows[0].CostInProducts.Should().BeApproximately(0.95m, 0.0000001m);
        rows[0].ShareOfAll.Should().BeApproximately(0.95m / 1.04m * 100m, 0.0000001m);
        rows[0].LargestIn.Should().Be("Pandesal", "it's all flour");
        rows[0].LargestShare.Should().BeApproximately(100m, 0.0000001m);
        rows[^1].CumulativeShare.Should().BeApproximately(100m, 0.0000001m, "every peso of these products' material cost is a purchased material's");
    });

    [Test]
    public Task Cost_drivers_leave_out_products_not_in_Production() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        await AddProductWithSubAssemblyAsync("Test Bun", doughRoleId, [(flourId, 1000m)], weightPerPiece: 10m);

        (await AnalyseCostDriversAsync()).Should().BeEmpty("a product still in Innovation isn't counted");
    });

    [Test]
    public Task Margin_erosion_compares_each_products_markup_then_with_now() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)], weightPerPiece: 10m);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Products.Single(p => p.Id == pandesalId).CurrentPrice = 1m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        // A version a month ago, at flour 0.05; flour has since gone up to 0.06.
        var reader = SavedProducts();
        var products = new ProductRepository(CreateDbContextFactory());
        var monthAgo = DateTime.Today.AddDays(-30);
        await new ProductVersionRepository(CreateDbContextFactory()).AddAsync(
            pandesalId, ProductVersionSnapshot.From((await products.GetByIdAsync(pandesalId, Cancellation))!, await reader.ReadDetailAsync(pandesalId, Cancellation)), monthAgo, null, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            var flour = dbContext.RawMaterials.Single(m => m.Id == flourId);
            flour.CostPerBaseUnit = 0.06m;
            flour.BufferedCostPerBaseUnit = 0.06m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var saved = await reader.ReadAllAsync(await products.GetAllAsync(Cancellation), CostBasis.Buffered, Cancellation);
        var versions = (await new ProductVersionRepository(CreateDbContextFactory()).GetLatestAsOfAsync(monthAgo.AddDays(1), Cancellation)).ToDictionary(v => v.ProductId);
        var row = MarginErosionAnalysis.Analyse(saved, versions).Should().ContainSingle().Subject;

        row.ProductName.Should().Be("Pandesal");
        row.BaseCostThen.Should().BeLessThan(row.BaseCostNow);
        row.MarkupThen.Should().BeGreaterThan(row.MarkupNow);
        row.HasDropped.Should().BeTrue();
        (await new ProductVersionRepository(CreateDbContextFactory()).GetLatestAsOfAsync(monthAgo.AddDays(-1), Cancellation))
            .Should().BeEmpty("there was no version before it");
    });

    [Test]
    public Task A_visit_reads_the_products_once_for_every_table_and_a_new_day_reads_only_the_versions() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughRoleId, [(flourId, 1000m)], weightPerPiece: 10m);
        await SetStageAsync([pandesalId], ProductStage.Production);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Products.Single(p => p.Id == pandesalId).CurrentPrice = 1m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        // A version a month ago, at flour 0.05; flour has since gone up to 0.06.
        var products = new ProductRepository(CreateDbContextFactory());
        var monthAgo = DateTime.Today.AddDays(-30);
        await new ProductVersionRepository(CreateDbContextFactory()).AddAsync(
            pandesalId, ProductVersionSnapshot.From((await products.GetByIdAsync(pandesalId, Cancellation))!, await SavedProducts().ReadDetailAsync(pandesalId, Cancellation)), monthAgo, null, Cancellation);
        await SetFlourCostAsync(flourId, 0.06m);
        var state = CreateReports();
        state.MarginErosionSince = monthAgo;

        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.MarginErosion.Count == 1, () => $"{state.MarginErosion.Count} eroded");

        var costs = state.ProductCosts.Should().ContainSingle().Subject;
        state.CostDrivers.Select(r => r.Name).Should().Equal("Flour");
        state.MarginErosion[0].BaseCostNow.Should().Be(costs.BaseCost, "both tables price the products the visit read");

        // Flour changes after the visit. Another day compares the products as the visit read them, beside the other tables,
        // until the next visit reads them again.
        await SetFlourCostAsync(flourId, 0.07m);
        state.MarginErosionSince = monthAgo.AddDays(-1);
        await Settle.UntilAsync(state, () => state.MarginErosion.Count == 0, () => $"{state.MarginErosion.Count} eroded");
        state.MarginErosionSince = monthAgo.AddDays(1);
        await Settle.UntilAsync(state, () => state.MarginErosion.Count == 1, () => $"{state.MarginErosion.Count} eroded");
        state.MarginErosion[0].BaseCostNow.Should().Be(costs.BaseCost);

        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.MarginErosion.Count == 1 && state.MarginErosion[0].BaseCostNow > costs.BaseCost, () => "not read again");
        state.MarginErosion[0].BaseCostNow.Should().Be(state.ProductCosts.Single().BaseCost);
    });

    private async Task SetFlourCostAsync(int flourId, decimal costPerGram)
    {
        await using var dbContext = CreateDbContext();
        var flour = dbContext.RawMaterials.Single(m => m.Id == flourId);
        flour.CostPerBaseUnit = costPerGram;
        flour.BufferedCostPerBaseUnit = costPerGram;
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private ReportsState CreateReports()
    {
        var factory = CreateDbContextFactory();
        return new ReportsState(
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialRepository(factory),
            new ProductRepository(factory),
            SavedProducts(),
            new Serilog.LoggerConfiguration().CreateLogger(),
            new ProductVersionRepository(factory),
            new IngredientBomLineRepository(factory));
    }

    private async Task<IReadOnlyList<CostDriverRow>> AnalyseCostDriversAsync()
    {
        var factory = CreateDbContextFactory();
        var products = await new ProductRepository(factory).GetAllAsync(Cancellation);
        var details = await SavedProducts().ReadDetailsAsync([.. products.Select(p => p.Id)], Cancellation);
        var materials = (await new RawMaterialRepository(factory).GetAllAsync(Cancellation)).ToDictionary(m => m.Id);
        var bomLines = await new IngredientBomLineRepository(factory).GetAllAsync(Cancellation);
        return CostDriverAnalysis.Analyse(products, details, materials, [.. bomLines]);
    }

    private async Task SetStageAsync(IReadOnlyList<int> productIds, ProductStage stage)
    {
        await using var dbContext = CreateDbContext();
        foreach (var product in dbContext.Products.Where(p => productIds.Contains(p.Id)))
        {
            product.Stage = stage;
        }

        await dbContext.SaveChangesAsync(Cancellation);
    }

    private SavedProductReader SavedProducts()
    {
        var factory = CreateDbContextFactory();
        return new SavedProductReader(
            new PackagingLinkRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            new PackagingLineRepository(factory),
            new CostingParametersRepository(factory),
            new SubAssemblyLineRepository(factory),
            new ProductRepository(factory),
            new CostingDefaultsRepository(factory),
            CreateUnitOfWork());
    }
}
