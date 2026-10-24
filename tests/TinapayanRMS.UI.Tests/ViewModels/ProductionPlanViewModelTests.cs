using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
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
    public async Task The_add_row_line_is_planned_as_it_is_typed_and_counted_once_committed()
    {
        var bakery = await AddBakeryAsync();
        var store = new InMemoryViewStateStore();
        var viewModel = CreateViewModel(store);
        await viewModel.LoadCommand.Execute();

        // Typed into the add row of an empty plan: not in Lines yet, but the tables follow it.
        var typed = viewModel.NewLine();
        typed.ProductId = bakery.UbeRollId;
        typed.Units = 4m;
        await WaitUntilSettledAsync(() => viewModel.Requisition.Count == 1, () => viewModel.ErrorMessage ?? "no list");
        viewModel.Mixes.Single().For.Should().Be("Ube Roll 4 pcs");
        viewModel.Lines.Should().BeEmpty();

        // Committed: the same line, counted once, and kept.
        viewModel.Lines.Add(typed);
        typed.Units = 6m;
        await WaitUntilSettledAsync(() => viewModel.Mixes.Single().For == "Ube Roll 6 pcs", () => viewModel.ErrorMessage ?? viewModel.Mixes.Single().For);
        store.ReadList(ProductionPlanViewModel.PlanKey).Should().Equal($"{bakery.UbeRollId}:6");

        // The next add row's line, typed into and then thrown away, leaves the plan.
        var dropped = viewModel.NewLine();
        dropped.ProductId = bakery.CheeseRollId;
        dropped.Units = 5m;
        await WaitUntilSettledAsync(() => viewModel.Mixes.Single().For.Contains("Cheese Roll", StringComparison.Ordinal), () => viewModel.Mixes.Single().For);
        viewModel.DropNewLine(dropped);
        await WaitUntilSettledAsync(() => viewModel.Mixes.Single().For == "Ube Roll 6 pcs", () => viewModel.Mixes.Single().For);
    }

    [Fact]
    public async Task Showing_the_tab_again_with_nothing_changed_keeps_every_row_and_writes_nothing()
    {
        var bakery = await AddBakeryAsync();
        var store = new InMemoryViewStateStore();
        var viewModel = CreateViewModel(store);
        await viewModel.LoadCommand.Execute();
        AddLine(viewModel, bakery.UbeRollId, 4m);
        AddLine(viewModel, bakery.CheeseRollId, 5m);
        await WaitUntilSettledAsync(() => viewModel.Mixes.Count == 1 && viewModel.Mixes[0].For.Contains("Cheese Roll", StringComparison.Ordinal), () => viewModel.ErrorMessage ?? "no mix");
        var products = viewModel.Products;
        var mix = viewModel.Mixes.Single();
        var flour = viewModel.Requisition.Single();
        var writes = store.WriteCount;

        await viewModel.LoadCommand.Execute();
        await WaitUntilSettledAsync(() => true, () => "never");

        store.WriteCount.Should().Be(writes, "re-pointing the lines at today's products isn't an edit");
        viewModel.Products.Should().BeSameAs(products, "no product changed, so the picker keeps its list");
        viewModel.Mixes.Single().Should().BeSameAs(mix);
        viewModel.Requisition.Single().Should().BeSameAs(flour);
    }

    [Fact]
    public async Task An_edit_replaces_only_the_rows_it_changes()
    {
        var bakery = await AddBakeryAsync();
        var sugarId = await AddIngredientAsync("Sugar", 0.08m);
        var toppingRoleId = await AddRoleAsync("Topping");
        var (toppedId, _) = await AddProductWithSubAssemblyAsync("Sugar Bun", toppingRoleId, [(sugarId, 500m)], weightPerPiece: 50m);
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();
        AddLine(viewModel, bakery.UbeRollId, 4m);
        var bun = viewModel.NewLine();
        viewModel.Lines.Add(bun);
        bun.ProductId = toppedId;
        bun.Units = 5m;
        await WaitUntilSettledAsync(() => viewModel.Mixes.Count == 2 && viewModel.Requisition.Count == 2, () => viewModel.ErrorMessage ?? $"{viewModel.Mixes.Count} mixes");
        var dough = viewModel.Mixes.Single(m => m.Name == "Ube Roll Dough");
        var flour = viewModel.Requisition.Single(r => r.MaterialId == bakery.FlourId);

        // 25 buns at 50 g are 1,250 g, 3 batches of 500 g where 5 buns were 1: only the bun's mix and its sugar change.
        bun.Units = 25m;
        await WaitUntilSettledAsync(() => viewModel.Mixes.Any(m => m.Batches == 3m), () => viewModel.ErrorMessage ?? "no change");

        viewModel.Mixes.Single(m => m.Name == "Ube Roll Dough").Should().BeSameAs(dough);
        viewModel.Requisition.Single(r => r.MaterialId == bakery.FlourId).Should().BeSameAs(flour);
        viewModel.Requisition.Single(r => r.MaterialId == sugarId).Needed.Should().Be(1500m);
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

    [Fact]
    public async Task A_mix_lists_its_ingredients_and_the_pieces_its_batches_leave_to_spare()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();

        // 4 Ube Rolls and 5 Cheese Rolls take 0.9 of the batch: the 100 g left are 1 more of either.
        AddLine(viewModel, bakery.UbeRollId, 4m);
        AddLine(viewModel, bakery.CheeseRollId, 5m);
        await WaitUntilSettledAsync(() => viewModel.Mixes.Count == 1 && viewModel.Mixes[0].For.Contains("Cheese Roll", StringComparison.Ordinal), () => viewModel.ErrorMessage ?? "no mix");

        var mix = viewModel.Mixes.Single();
        mix.Spare.Should().Be("Ube Roll 1 pcs or Cheese Roll 1 pcs");
        mix.Ingredients.Should().Equal(new MixIngredientRow("Flour", 1000m, 1000m));

        // 3 Ube Rolls alone take 0.3 of it: the rest are 7 more.
        viewModel.Lines.Remove(viewModel.Lines.Single(l => l.ProductId == bakery.CheeseRollId));
        viewModel.Lines.Single().Units = 3m;
        await WaitUntilSettledAsync(() => viewModel.Mixes.Single().Spare == "7 pcs", () => viewModel.Mixes.Single().Spare);
    }

    [Fact]
    public async Task The_plan_sells_at_each_products_current_price_or_its_worked_out_one_and_says_which()
    {
        var bakery = await AddBakeryAsync();
        await SetCurrentPriceAsync(bakery.UbeRollId, 12m);
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();

        AddLine(viewModel, bakery.UbeRollId, 4m);
        AddLine(viewModel, bakery.CheeseRollId, 5m);
        await WaitUntilSettledAsync(() => viewModel.SalesNote is not null, () => viewModel.ErrorMessage ?? "no sales");

        var ube = viewModel.Lines.Single(l => l.ProductId == bakery.UbeRollId);
        var cheese = viewModel.Lines.Single(l => l.ProductId == bakery.CheeseRollId);
        ube.Sales.Should().Be(48m, "4 at the Current Price of 12");
        ube.HasNoCurrentPrice.Should().BeFalse();
        cheese.HasNoCurrentPrice.Should().BeTrue();
        cheese.Sales.Should().BePositive("it sells at the selling price its costing works out");
        viewModel.TotalSales.Should().Be(ube.Sales!.Value + cheese.Sales!.Value);
        viewModel.TotalProfit.Should().Be(ube.Profit!.Value + cheese.Profit!.Value);
        viewModel.ProfitMarginPercent.Should().Be(CostingRules.PercentOf(
            viewModel.TotalProfit, ube.SalesFigures!.SalesBeforeVat + cheese.SalesFigures!.SalesBeforeVat));
        viewModel.SalesNote.Should().Contain("Cheese Roll").And.NotContain("Ube Roll");
    }

    [Fact]
    public async Task The_requisition_says_what_the_whole_units_to_buy_cost()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();

        // 12 Ube Rolls in half batches are 1.5 batches, 1,500 g of flour: 2 kilos at 50, where what's needed costs 75.
        viewModel.HalfBatches = true;
        AddLine(viewModel, bakery.UbeRollId, 12m);
        await WaitUntilSettledAsync(() => viewModel.Requisition.Count == 1, () => viewModel.ErrorMessage ?? "no list");

        var flour = viewModel.Requisition.Single();
        flour.Cost.Should().Be(75m);
        flour.Buy.Should().Be(2m);
        flour.BuyCost.Should().Be(100m);
        viewModel.TotalCost.Should().Be(75m);
        viewModel.TotalBuyCost.Should().Be(100m);
    }

    [Fact]
    public async Task Half_batches_round_each_mix_up_to_the_next_half_and_are_kept()
    {
        var bakery = await AddBakeryAsync();
        var store = new InMemoryViewStateStore();
        var viewModel = CreateViewModel(store);
        await viewModel.LoadCommand.Execute();

        // 12 Ube Rolls take 1.2 batches of the 1,000 g dough: 2 whole ones, or 1.5 in halves.
        AddLine(viewModel, bakery.UbeRollId, 12m);
        await WaitUntilSettledAsync(() => viewModel.Mixes.Count == 1, () => viewModel.ErrorMessage ?? "no mix");
        viewModel.Mixes.Single().Batches.Should().Be(2m);

        viewModel.HalfBatches = true;
        await WaitUntilSettledAsync(() => viewModel.Mixes.Single().Batches == 1.5m, () => $"{viewModel.Mixes.Single().Batches} batches");
        viewModel.Mixes.Single().Grams.Should().Be(1500m);
        viewModel.Mixes.Single().Spare.Should().Be("3 pcs");
        store.ReadList(ProductionPlanViewModel.HalfBatchesKey).Should().Equal("true");

        var next = CreateViewModel(store);
        next.HalfBatches.Should().BeTrue("it's kept for next time, as the plan is");
    }

    [Fact]
    public async Task A_plan_saved_under_a_name_opens_again_with_its_half_batches_and_is_kept_open()
    {
        var bakery = await AddBakeryAsync();
        var store = new InMemoryViewStateStore();
        var viewModel = CreateViewModel(store);
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput("Saturday"));
        await viewModel.LoadCommand.Execute();
        AddLine(viewModel, bakery.UbeRollId, 40m);
        AddLine(viewModel, bakery.CheeseRollId, 25m);
        viewModel.HalfBatches = true;

        await viewModel.SavePlanCommand.Execute();

        var saved = viewModel.Plans.Should().ContainSingle().Subject;
        saved.Name.Should().Be("Saturday");
        viewModel.SelectedPlan.Should().BeSameAs(saved);
        viewModel.HasUnsavedChanges.Should().BeFalse();

        // Cleared, the plan closes; opened again, it's back as it was saved.
        await viewModel.ClearCommand.Execute();
        viewModel.HalfBatches = false;
        viewModel.SelectedPlan.Should().BeNull();
        await viewModel.OpenPlanCommand.Execute(saved);

        viewModel.Lines.Select(l => (l.ProductId, l.Units)).Should().Equal((bakery.UbeRollId, 40m), (bakery.CheeseRollId, 25m));
        viewModel.HalfBatches.Should().BeTrue();
        viewModel.SelectedPlan.Should().BeSameAs(saved);
        store.ReadList(ProductionPlanViewModel.PlanKey).Should().Equal($"{bakery.UbeRollId}:40", $"{bakery.CheeseRollId}:25");

        // Next run, the plan is still the one open, and an edit since is a change to it.
        var next = CreateViewModel(store);
        await next.LoadCommand.Execute();
        next.SelectedPlan!.Id.Should().Be(saved.Id);
        next.HasUnsavedChanges.Should().BeFalse();
        next.Lines[0].Units = 41m;
        next.HasUnsavedChanges.Should().BeTrue();
    }

    [Fact]
    public async Task Opening_a_plan_over_one_not_saved_asks_first_and_keeping_it_keeps_the_table()
    {
        var bakery = await AddBakeryAsync();
        var plans = new SavedPlanRepository(CreateDbContextFactory());
        await plans.SaveAsync("Weekday", [new SavedPlanLine { ProductId = bakery.CheeseRollId, Units = 10m }], false, DateTime.Now, Cancellation);
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        string? asked = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(false);
        });
        await viewModel.LoadCommand.Execute();
        AddLine(viewModel, bakery.UbeRollId, 4m);

        await viewModel.OpenPlanCommand.Execute(viewModel.Plans.Single());

        asked.Should().Be("Open the saved plan Weekday? The plan in the table isn't saved, and it replaces it.");
        viewModel.Lines.Single().ProductId.Should().Be(bakery.UbeRollId);
        viewModel.SelectedPlan.Should().BeNull();
    }

    [Fact]
    public async Task Opening_a_plan_leaves_out_a_product_deleted_since_and_says_so()
    {
        var bakery = await AddBakeryAsync();
        var plans = new SavedPlanRepository(CreateDbContextFactory());
        await plans.SaveAsync(
            "Saturday",
            [new SavedPlanLine { ProductId = bakery.UbeRollId, Units = 10m }, new SavedPlanLine { ProductId = bakery.BoxId, Units = 5m }],
            false,
            DateTime.Now,
            Cancellation);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(bakery.BoxId, Cancellation);
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();

        await viewModel.OpenPlanCommand.Execute(viewModel.Plans.Single());

        viewModel.Lines.Select(l => l.ProductId).Should().Equal(bakery.UbeRollId);
        viewModel.SuccessMessage.Should().Be("Opened Saturday, leaving out 1 product deleted since.");
    }

    [Fact]
    public async Task Deleting_the_open_plan_closes_it_and_leaves_its_products_in_the_table()
    {
        var bakery = await AddBakeryAsync();
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput("Saturday"));
        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        await viewModel.LoadCommand.Execute();
        AddLine(viewModel, bakery.UbeRollId, 40m);
        await viewModel.SavePlanCommand.Execute();

        await viewModel.DeletePlanCommand.Execute();

        viewModel.Plans.Should().BeEmpty();
        viewModel.SelectedPlan.Should().BeNull();
        viewModel.Lines.Single().ProductId.Should().Be(bakery.UbeRollId);
        (await new SavedPlanRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Saving_an_empty_plan_is_refused()
    {
        await AddBakeryAsync();
        var viewModel = CreateViewModel(new InMemoryViewStateStore());
        await viewModel.LoadCommand.Execute();

        await viewModel.SavePlanCommand.Execute();

        viewModel.ErrorMessage.Should().Be(SavedPlanRules.NoLinesText);
        viewModel.Plans.Should().BeEmpty();
    }

    private async Task SetCurrentPriceAsync(int productId, decimal price)
    {
        await using var dbContext = CreateDbContext();
        var product = await dbContext.Products.SingleAsync(p => p.Id == productId, Cancellation);
        product.CurrentPrice = price;
        await dbContext.SaveChangesAsync(Cancellation);
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
            new SavedPlanRepository(factory),
            new SavedProductReader(
                new PackagingLinkRepository(factory),
                new ProductSubAssemblyUsageRepository(factory),
                new PackagingLineRepository(factory),
                new CostingParametersRepository(factory),
                new SubAssemblyLineRepository(factory),
                new ProductRepository(factory),
                new CostingDefaultsRepository(factory)),
            store,
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>The plan is worked out in the background: waits until <paramref name="condition"/> has held for a moment.</summary>
    private static async Task WaitUntilSettledAsync(Func<bool> condition, Func<string> describe)
    {
        // With no dispatcher in a test, the tables are merged on a pool thread, so a condition enumerating one can meet a
        // merge under way ("Collection was modified"): the plan hasn't settled then. In the app both are on the UI thread.
        bool Holds()
        {
            try
            {
                return condition();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!Holds())
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
