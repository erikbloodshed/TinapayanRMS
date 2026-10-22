using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The Price Simulator screen against a real database: a price change reaches made and linked ingredients and composites as
/// a real one would, an untouched product prices exactly as Reports does, and nothing is saved.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class PriceSimulatorViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>
    /// Egg at 0.014 a gram (priced per gram), egg white linked to it (70% of its weight, 30% of its cost), and flour at 0.05.
    /// Meringue is 50 g a piece of a dough of egg white, Pandesal 50 g a piece of a flour dough, and the Party Box holds two
    /// Meringues and a Pandesal. No costing, so each base cost is its materials.
    /// </summary>
    private async Task<Bakery> AddBakeryAsync()
    {
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

        var (meringueId, _) = await AddProductWithSubAssemblyAsync("Meringue", doughId, [(whiteId, 1000m)], weightPerPiece: 50m);
        var (pandesalId, _) = await AddProductWithSubAssemblyAsync("Pandesal", doughId, [(flourId, 1000m)], weightPerPiece: 50m);
        var products = new ProductRepository(CreateDbContextFactory());
        var boxId = await products.SaveAsync(
            new Product { Name = "Party Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(meringueId, 2), (pandesalId, 1)] },
            null,
            null,
            null,
            Cancellation);
        return new Bakery(eggId, whiteId, flourId, meringueId, pandesalId, boxId);
    }

    private sealed record Bakery(int EggId, int WhiteId, int FlourId, int MeringueId, int PandesalId, int BoxId);

    [Fact]
    public async Task With_no_changes_every_product_prices_as_Reports_does()
    {
        await AddBakeryAsync();
        var viewModel = CreateViewModel();
        var reports = CreateReportsViewModel();

        await viewModel.LoadCommand.Execute();
        await reports.LoadCommand.Execute();
        await WaitUntilSettledAsync(() => viewModel.Products.Count == 3, () => $"{viewModel.Products.Count} rows");

        foreach (var row in viewModel.Products)
        {
            var report = reports.ProductCosts.Single(r => r.ProductId == row.ProductId);
            row.BaseCost.Should().Be(report.BaseCost);
            row.SimulatedBaseCost.Should().Be(report.BaseCost);
            row.RawMaterialCost.Should().Be(report.IngredientsCost + report.PackagingCost);
            row.SimulatedRawMaterialCost.Should().Be(row.RawMaterialCost);
            row.SuggestedPrice.Should().Be(report.SuggestedPrice);
            row.IsAffected.Should().BeFalse();
        }

        viewModel.Materials.Select(m => m.Name).Should().Equal("Egg", "Flour");
    }

    [Fact]
    public async Task A_rise_in_egg_reaches_the_linked_egg_white_its_products_and_the_composites_holding_them()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var change = viewModel.NewChange();
        viewModel.Changes.Add(change);
        change.MaterialId = bakery.EggId;
        change.ChangePercent = 10m;
        await WaitUntilSettledAsync(() => Row(viewModel, bakery.MeringueId)?.IsAffected == true, () => viewModel.ErrorMessage ?? "not yet");

        change.UnitName.Should().Be("gram");
        change.CurrentPrice.Should().Be(0.014m);
        change.NewPrice.Should().Be(0.0154m);
        var meringue = Row(viewModel, bakery.MeringueId)!;
        var pandesal = Row(viewModel, bakery.PandesalId)!;
        var box = Row(viewModel, bakery.BoxId)!;
        meringue.SimulatedBaseCost.Should().BeApproximately(meringue.BaseCost * 1.1m, 0.0000001m, "50 g of egg white, which costs 10% more with the egg");
        meringue.ChangePercent.Should().BeApproximately(10m, 0.00001m);
        meringue.SimulatedRawMaterialCost.Should().BeApproximately(meringue.RawMaterialCost * 1.1m, 0.0000001m, "its only material is the egg white");
        meringue.RawMaterialChangePercent.Should().BeApproximately(10m, 0.00001m);
        pandesal.IsAffected.Should().BeFalse();
        pandesal.SimulatedBaseCost.Should().Be(pandesal.BaseCost);
        pandesal.SimulatedRawMaterialCost.Should().Be(pandesal.RawMaterialCost);
        pandesal.RawMaterialChangePercent.Should().Be(0m);
        box.SimulatedBaseCost.Should().BeApproximately(box.BaseCost + 2 * (meringue.SimulatedBaseCost - meringue.BaseCost), 0.0000001m,
            "the box holds two meringues");

        viewModel.OnlyAffected = true;
        viewModel.Products.Select(r => r.ProductName).Should().BeEquivalentTo(["Meringue", "Party Box"]);

        await using var dbContext = CreateDbContext();
        dbContext.RawMaterials.Single(m => m.Id == bakery.EggId).CostPerBaseUnit.Should().Be(0.014m, "nothing is saved");
    }

    [Fact]
    public async Task The_report_sets_each_product_beside_today_with_the_changes_that_reach_it()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        var change = viewModel.NewChange();
        viewModel.Changes.Add(change);
        change.MaterialId = bakery.EggId;
        change.ChangePercent = 10m;
        await WaitUntilSettledAsync(() => Row(viewModel, bakery.MeringueId)?.IsAffected == true, () => viewModel.ErrorMessage ?? "not yet");

        var choices = viewModel.PrintChoices(new DateTime(2026, 10, 6, 9, 0, 0));

        var shown = choices.Should().ContainSingle("nothing is picked in the table").Subject;
        shown.Label.Should().Be("All products shown (3)");
        shown.Set.Title.Should().Be(PriceSimulatorReport.Title);
        var egg = new PrintedPriceChange("Egg", "gram", 0.014m, 10m, 0.0154m);
        shown.Set.PriceChanges.Should().Equal(egg);
        shown.Set.Products.Select(p => p.ProductName).Should().Equal(viewModel.Products.Select(r => r.ProductName), "in the table's order");

        var meringue = shown.Set.Products.Single(p => p.ProductName == "Meringue");
        var meringueRow = Row(viewModel, bakery.MeringueId)!;
        meringue.Simulation!.Changes.Should().Equal(egg);
        meringue.Simulation.Now.BaseCost.Should().Be(meringueRow.BaseCost);
        meringue.Costs.BaseCost.Should().Be(meringueRow.SimulatedBaseCost);
        var white = meringue.Sections.Single().Lines.Single();
        white.Ingredient.Should().Be("Egg White");
        white.NowCost.Should().NotBeNull().And.BeLessThan(white.Cost, "the egg white costs more with the egg");
        meringue.Sections.Single().NowBatchCost.Should().NotBeNull();

        var pandesal = shown.Set.Products.Single(p => p.ProductName == "Pandesal");
        pandesal.Simulation!.Changes.Should().BeEmpty("no change reaches flour");
        pandesal.Sections.Single().Lines.Should().OnlyContain(l => l.NowCost == null);
        pandesal.Sections.Single().NowBatchCost.Should().BeNull();

        var box = shown.Set.Products.Single(p => p.ProductName == "Party Box");
        box.Simulation!.Changes.Should().Equal([egg], "it holds meringues");
        box.Components!.Single(c => c.Product == "Meringue").NowCost.Should().NotBeNull();
        box.Components!.Single(c => c.Product == "Pandesal").NowCost.Should().BeNull();
    }

    [Fact]
    public async Task Picked_products_print_on_their_own_in_the_tables_order_and_stay_picked_through_a_change()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel();
        IReadOnlyList<PrintChoice>? printed = null;
        viewModel.ShowPrint.RegisterHandler(interaction =>
        {
            printed = interaction.Input;
            interaction.SetOutput(ReactiveUI.Primitives.RxVoid.Default);
        });
        await viewModel.LoadCommand.Execute();
        await WaitUntilSettledAsync(() => viewModel.Products.Count == 3, () => $"{viewModel.Products.Count} rows");

        viewModel.SelectedProducts.Add(Row(viewModel, bakery.PandesalId)!);
        viewModel.SelectedProducts.Add(Row(viewModel, bakery.MeringueId)!);
        await viewModel.PrintCommand.Execute();

        printed.Should().NotBeNull();
        printed!.Select(c => c.Label).Should().Equal("Selected products (2)", "All products shown (3)");
        printed[0].Set.Products.Select(p => p.ProductName).Should().Equal(
            viewModel.Products.Where(r => r.ProductId is var id && (id == bakery.PandesalId || id == bakery.MeringueId)).Select(r => r.ProductName),
            "in the table's order, not the order they were picked in");
        printed[0].Set.PriceChanges.Should().BeEmpty();

        // A change makes new rows, and the products picked are picked again among them; ticking Only affected hides Pandesal.
        var change = viewModel.NewChange();
        viewModel.Changes.Add(change);
        change.MaterialId = bakery.EggId;
        change.ChangePercent = 10m;
        await WaitUntilSettledAsync(() => Row(viewModel, bakery.MeringueId)?.IsAffected == true, () => viewModel.ErrorMessage ?? "not yet");
        viewModel.SelectedProducts.Should().BeEquivalentTo([Row(viewModel, bakery.PandesalId), Row(viewModel, bakery.MeringueId)]);

        viewModel.OnlyAffected = true;
        viewModel.SelectedProducts.Should().Equal(Row(viewModel, bakery.MeringueId)!);
        viewModel.PrintChoices(DateTime.Now).Select(c => c.Label).Should().Equal("Selected products (1)", "All products shown (2)");
    }

    [Fact]
    public async Task A_new_price_works_out_the_change_and_the_other_way_round()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();
        var change = viewModel.NewChange();
        viewModel.Changes.Add(change);
        change.MaterialId = bakery.EggId;

        change.NewPrice = 0.0105m;
        change.ChangePercent.Should().Be(-25m);

        change.ChangePercent = 50m;
        change.NewPrice.Should().Be(0.021m);
    }

    [Fact]
    public async Task A_price_that_cant_be_is_refused_and_says_why()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.Execute();

        var flour = viewModel.NewChange();
        viewModel.Changes.Add(flour);
        flour.MaterialId = bakery.FlourId;
        flour.ChangePercent = 10m;

        await WaitUntilSettledAsync(() => viewModel.ErrorMessage is not null, () => "no message");
        viewModel.ErrorMessage.Should().Be($"Flour: {PriceChangeRules.NoPriceToChangeText}");

        flour.NewPrice = 0.06m;
        await WaitUntilSettledAsync(() => viewModel.ErrorMessage is null && Row(viewModel, bakery.PandesalId)?.IsAffected == true, () => viewModel.ErrorMessage ?? "not yet");
    }

    [Fact]
    public async Task A_double_clicked_product_asks_to_be_opened()
    {
        var viewModel = CreateViewModel();
        List<int> opened = [];
        using var subscription = viewModel.ProductOpenRequests.Subscribe(opened.Add);

        await viewModel.OpenProductCommand.Execute(42);

        opened.Should().Equal(42);
    }

    [Fact]
    public async Task Export_asks_for_the_Price_Simulator_workbook()
    {
        var viewModel = CreateViewModel();
        string? asked = null;
        viewModel.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(@"C:\Reports\Price Simulator 2026-10-04.xlsx");
        });

        await viewModel.ExportCommand.Execute();

        asked.Should().Be(PriceSimulatorViewModel.ExportTitle);
        viewModel.SuccessMessage.Should().Be(@"Saved Price Simulator 2026-10-04.xlsx in C:\Reports.");
    }

    private static PriceSimulatorProductRow? Row(PriceSimulatorViewModel viewModel, int productId) => viewModel.Products.FirstOrDefault(r => r.ProductId == productId);

    private PriceSimulatorViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        return new PriceSimulatorViewModel(
            new ProductRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            SavedProducts(factory),
            new LoggerConfiguration().CreateLogger());
    }

    private ReportsViewModel CreateReportsViewModel()
    {
        var factory = CreateDbContextFactory();
        return new ReportsViewModel(
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialRepository(factory),
            new ProductRepository(factory),
            SavedProducts(factory),
            new LoggerConfiguration().CreateLogger());
    }

    private static SavedProductReader SavedProducts(Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.TinapayanRMSDbContext> factory) => new(
        new PackagingLinkRepository(factory),
        new ProductSubAssemblyUsageRepository(factory),
        new PackagingLineRepository(factory),
        new CostingParametersRepository(factory),
        new SubAssemblyLineRepository(factory),
        new ProductRepository(factory));

    /// <summary>The figures are worked out in the background: waits until <paramref name="condition"/> has held for a moment.</summary>
    private static async Task WaitUntilSettledAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!condition())
            {
                heldFor.Restart();
            }
            else if (heldFor.Elapsed > TimeSpan.FromMilliseconds(300))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"The figures never settled: {describe()}.");
    }
}
