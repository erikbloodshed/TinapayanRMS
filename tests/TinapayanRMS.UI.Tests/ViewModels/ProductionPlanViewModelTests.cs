using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The Production Plan screen against a real database: products sharing a dough share its batches, the purchase requisition
/// buys in whole purchase units, and the plan is kept between runs.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductionPlanViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>
    /// Flour priced at 50 a kilo. Ube Roll is 100 g a piece of a 1,000 g flour dough; Cheese Roll links the same dough at
    /// 100 g a piece; the Party Box holds 2 Ube Rolls and 1 Cheese Roll.
    /// </summary>
    private async Task<Bakery> AddBakeryAsync()
    {
        var doughRoleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var kiloId = await AddKilogramUnitAsync(flourId);
        await SetPriceAsync(flourId, kiloId, 50m);
        var (ubeRollId, doughId) = await AddProductWithSubAssemblyAsync("Ube Roll", doughRoleId, [(flourId, 1000m)], weightPerPiece: 100m);
        var cheeseRollId = await AddProductAsync("Cheese Roll");
        await SaveRecipeAsync(cheeseRollId, new RecipeDraft([], [new LinkedUsageDraft(doughId, UsageAuthoredField.WeightPerPiece, 100m)]));
        var boxId = await new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = "Party Box", IsComposite = true },
            new RecipeDraft([], []) { Components = [(ubeRollId, 2), (cheeseRollId, 1)] },
            null,
            null,
            null,
            Cancellation);
        return new Bakery(flourId, ubeRollId, cheeseRollId, boxId);
    }

    private sealed record Bakery(int FlourId, int UbeRollId, int CheeseRollId, int BoxId);

    [Fact]
    public async Task Products_sharing_a_dough_share_its_batches_and_the_flour_is_bought_by_the_kilo()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();

        // 4 Ube Rolls and 5 Cheese Rolls at 100 g are 0.4 and 0.5 of a 1,000 g batch: 1 batch between them.
        AddLine(viewModel, bakery.UbeRollId, 4m);
        AddLine(viewModel, bakery.CheeseRollId, 5m);
        await WaitUntilSettledAsync(() => viewModel.Mixes.Count == 1 && viewModel.Mixes[0].For.Contains("Cheese Roll", StringComparison.Ordinal), () => viewModel.ErrorMessage ?? $"{viewModel.Mixes.Count} mixes");

        var mix = viewModel.Mixes.Single();
        mix.Name.Should().Be("Ube Roll Dough");
        mix.Batches.Should().Be(1m);
        mix.Grams.Should().Be(1000m);
        mix.For.Should().Be("Ube Roll 4 pcs, Cheese Roll 5 pcs");
        var flour = viewModel.Requisition.Should().ContainSingle().Subject;
        flour.Needed.Should().Be(1000m);
        flour.Unit.Should().Be("g");
        flour.Buy.Should().Be(1m);
        flour.BuyUnit.Should().Be("kilogram");
        flour.Cost.Should().Be(50m);
        viewModel.TotalCost.Should().Be(50m);
        viewModel.Notes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_composite_makes_the_products_it_holds()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();

        AddLine(viewModel, bakery.BoxId, 10m);
        await WaitUntilSettledAsync(() => viewModel.Mixes.Count == 1, () => viewModel.ErrorMessage ?? "no mix");

        viewModel.Mixes.Single().For.Should().Be("Ube Roll 20 pcs, Cheese Roll 10 pcs");
        viewModel.Mixes.Single().Batches.Should().Be(3m, "30 pieces of 100 g from 1,000 g batches");
        viewModel.Lines.Single().PiecesText.Should().BeEmpty("a composite is one of itself");
    }

    [Fact]
    public async Task The_plan_is_kept_between_runs_and_a_product_gone_since_is_left_out()
    {
        var bakery = await AddBakeryAsync();
        var store = new InMemoryViewStateStore();
        var first = CreateViewModel(store);
        await first.LoadCommand.Execute();
        AddLine(first, bakery.UbeRollId, 12m);
        AddLine(first, bakery.CheeseRollId, 3.5m);

        store.ReadList(ProductionPlanViewModel.PlanKey).Should().Equal($"{bakery.UbeRollId}:12", $"{bakery.CheeseRollId}:3.5");

        store.WriteList(ProductionPlanViewModel.PlanKey, [.. store.ReadList(ProductionPlanViewModel.PlanKey)!, "999999:5"]);
        var next = CreateViewModel(store);
        await next.LoadCommand.Execute();

        next.Lines.Select(l => (l.ProductName, l.Units)).Should().Equal(("Ube Roll", 12m), ("Cheese Roll", 3.5m));
        next.Lines[0].PiecesText.Should().Be("12 pcs");
    }

    [Fact]
    public async Task Print_hands_over_the_plan_as_shown()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();
        AddLine(viewModel, bakery.UbeRollId, 4m);
        await WaitUntilSettledAsync(() => viewModel.Requisition.Count == 1, () => viewModel.ErrorMessage ?? "no list");
        ProductPrintSet? printed = null;
        viewModel.ShowPrint.RegisterHandler(interaction =>
        {
            printed = interaction.Input;
            interaction.SetOutput(ReactiveUI.Primitives.RxVoid.Default);
        });

        await viewModel.PrintCommand.Execute();

        printed!.Plan!.Orders.Should().ContainSingle().Which.ProductName.Should().Be("Ube Roll");
        printed.Plan.Mixes.Should().Equal(viewModel.Mixes);
        printed.Plan.Requisition.Should().Equal(viewModel.Requisition);
        printed.Title.Should().Be("Production Plan");
    }

    private static void AddLine(ProductionPlanViewModel viewModel, int productId, decimal units)
    {
        var line = viewModel.NewLine();
        viewModel.Lines.Add(line);
        line.ProductId = productId;
        line.Units = units;
    }

    private ProductionPlanViewModel CreateViewModel(IViewStateStore store)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        return new ProductionPlanViewModel(
            new ProductRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                new ProductRepository(factory)),
            store,
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>The plan is worked out in the background: waits until <paramref name="condition"/> has held for a moment.</summary>
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

        throw new TimeoutException($"The plan never settled: {describe()}.");
    }
}
