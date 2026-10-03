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
/// The What-If Prices screen against a real database: a price change reaches made and linked ingredients and composites as
/// a real one would, an untouched product prices exactly as Reports does, and nothing is saved.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class WhatIfViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
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
            row.WhatIfBaseCost.Should().Be(report.BaseCost);
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
        meringue.WhatIfBaseCost.Should().BeApproximately(meringue.BaseCost * 1.1m, 0.0000001m, "50 g of egg white, which costs 10% more with the egg");
        meringue.ChangePercent.Should().BeApproximately(10m, 0.00001m);
        pandesal.IsAffected.Should().BeFalse();
        pandesal.WhatIfBaseCost.Should().Be(pandesal.BaseCost);
        box.WhatIfBaseCost.Should().BeApproximately(box.BaseCost + 2 * (meringue.WhatIfBaseCost - meringue.BaseCost), 0.0000001m,
            "the box holds two meringues");

        viewModel.OnlyAffected = true;
        viewModel.Products.Select(r => r.ProductName).Should().BeEquivalentTo(["Meringue", "Party Box"]);

        await using var dbContext = CreateDbContext();
        dbContext.RawMaterials.Single(m => m.Id == bakery.EggId).CostPerBaseUnit.Should().Be(0.014m, "nothing is saved");
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
    public async Task Export_asks_for_the_What_If_workbook()
    {
        var viewModel = CreateViewModel();
        string? asked = null;
        viewModel.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(@"C:\Reports\What-If Prices 2026-10-04.xlsx");
        });

        await viewModel.ExportCommand.Execute();

        asked.Should().Be(WhatIfViewModel.ExportTitle);
        viewModel.SuccessMessage.Should().Be(@"Saved What-If Prices 2026-10-04.xlsx in C:\Reports.");
    }

    private static WhatIfProductRow? Row(WhatIfViewModel viewModel, int productId) => viewModel.Products.FirstOrDefault(r => r.ProductId == productId);

    private WhatIfViewModel CreateViewModel()
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        return new WhatIfViewModel(
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
