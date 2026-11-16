using System.Collections.Specialized;
using System.Reactive.Linq;
using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Pricing;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.PriceSimulator;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Reports;

namespace TinapayanRMS.Presentation.Tests.PriceSimulator;

/// <summary>
/// The Price Simulator screen against a real database: a price change reaches made and linked ingredients and composites as
/// a real one would, an untouched product prices exactly as Reports does, and nothing is saved.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class PriceSimulatorStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
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

    [Test]
    public Task With_no_changes_every_product_prices_as_Reports_does() => UiThread.RunAsync(async () =>
    {
        await AddBakeryAsync();
        var state = CreateState();
        var reports = CreateReportsState();

        await state.LoadCommand.ExecuteAsync();
        await reports.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Products.Count == 3, () => $"{state.Products.Count} rows");

        foreach (var row in state.Products)
        {
            var report = reports.ProductCosts.Single(r => r.ProductId == row.ProductId);
            row.BaseCost.Should().Be(report.BaseCost);
            row.SimulatedBaseCost.Should().Be(report.BaseCost);
            row.RawMaterialCost.Should().Be(report.IngredientsCost + report.PackagingCost);
            row.SimulatedRawMaterialCost.Should().Be(row.RawMaterialCost);
            row.SuggestedPrice.Should().Be(report.SuggestedPrice);
            row.IsAffected.Should().BeFalse();
        }

        state.Materials.Select(m => m.Name).Should().Equal("Egg", "Flour");
    });

    [Test]
    public Task A_change_typed_and_the_filter_ticked_each_reach_the_products_table_in_one_change() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Products.Count == 3, () => $"{state.Products.Count} rows");

        // Its grid draws every row again for each change it hears of: a row at a time, a change held the window over a second.
        List<NotifyCollectionChangedAction> heard = [];
        state.Products.CollectionChanged += (_, e) => heard.Add(e.Action);

        // The row's material picked prices nothing yet, so the table is left as it was; the percent typed changes it once.
        var change = state.NewChange();
        state.Changes.Add(change);
        change.MaterialId = bakery.EggId;
        change.ChangePercent = 10m;
        await Settle.UntilAsync(state, () => Row(state, bakery.MeringueId)?.IsAffected == true, () => state.ErrorMessage ?? "not yet");
        heard.Should().Equal(NotifyCollectionChangedAction.Reset);

        state.OnlyAffected = true;
        heard.Should().Equal(NotifyCollectionChangedAction.Reset, NotifyCollectionChangedAction.Reset);
        state.Products.Select(r => r.ProductName).Should().BeEquivalentTo(["Meringue", "Party Box"]);
    });

    [Test]
    public Task A_rise_in_egg_reaches_the_linked_egg_white_its_products_and_the_composites_holding_them() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        var change = state.NewChange();
        state.Changes.Add(change);
        change.MaterialId = bakery.EggId;
        change.ChangePercent = 10m;
        await Settle.UntilAsync(state, () => Row(state, bakery.MeringueId)?.IsAffected == true, () => state.ErrorMessage ?? "not yet");

        change.UnitName.Should().Be("gram");
        change.CurrentPrice.Should().Be(0.014m);
        change.NewPrice.Should().Be(0.0154m);
        var meringue = Row(state, bakery.MeringueId)!;
        var pandesal = Row(state, bakery.PandesalId)!;
        var box = Row(state, bakery.BoxId)!;
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

        state.OnlyAffected = true;
        state.Products.Select(r => r.ProductName).Should().BeEquivalentTo(["Meringue", "Party Box"]);

        await using var dbContext = CreateDbContext();
        dbContext.RawMaterials.Single(m => m.Id == bakery.EggId).CostPerBaseUnit.Should().Be(0.014m, "nothing is saved");
    });

    [Test]
    public Task The_report_sets_each_product_beside_today_with_the_changes_that_reach_it() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var change = state.NewChange();
        state.Changes.Add(change);
        change.MaterialId = bakery.EggId;
        change.ChangePercent = 10m;
        await Settle.UntilAsync(state, () => Row(state, bakery.MeringueId)?.IsAffected == true, () => state.ErrorMessage ?? "not yet");

        var choices = state.PrintChoices(new DateTime(2026, 10, 6, 9, 0, 0));

        var shown = choices.Should().ContainSingle("nothing is picked in the table").Subject;
        shown.Label.Should().Be("All products shown (3)");
        shown.Set.Title.Should().Be(PriceSimulatorReport.Title);
        var egg = new PrintedPriceChange("Egg", "gram", 0.014m, 10m, 0.0154m);
        shown.Set.PriceChanges.Should().Equal(egg);
        shown.Set.Products.Select(p => p.ProductName).Should().Equal(state.Products.Select(r => r.ProductName), "in the table's order");

        var meringue = shown.Set.Products.Single(p => p.ProductName == "Meringue");
        var meringueRow = Row(state, bakery.MeringueId)!;
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
    });

    [Test]
    public Task Picked_products_print_on_their_own_in_the_tables_order_and_stay_picked_through_a_change() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = CreateState();
        IReadOnlyList<PrintChoice>? printed = null;
        state.ShowPrint.RegisterHandler(interaction =>
        {
            printed = interaction.Input;
            interaction.SetOutput(System.Reactive.Unit.Default);
        });
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Products.Count == 3, () => $"{state.Products.Count} rows");

        state.SelectedProducts.Add(Row(state, bakery.PandesalId)!);
        state.SelectedProducts.Add(Row(state, bakery.MeringueId)!);
        await state.PrintCommand.ExecuteAsync();

        printed.Should().NotBeNull();
        printed!.Select(c => c.Label).Should().Equal("Selected products (2)", "All products shown (3)");
        printed[0].Set.Products.Select(p => p.ProductName).Should().Equal(
            state.Products.Where(r => r.ProductId is var id && (id == bakery.PandesalId || id == bakery.MeringueId)).Select(r => r.ProductName),
            "in the table's order, not the order they were picked in");
        printed[0].Set.PriceChanges.Should().BeEmpty();

        // A change makes new rows, and the products picked are picked again among them; ticking Only affected hides Pandesal.
        var change = state.NewChange();
        state.Changes.Add(change);
        change.MaterialId = bakery.EggId;
        change.ChangePercent = 10m;
        await Settle.UntilAsync(state, () => Row(state, bakery.MeringueId)?.IsAffected == true, () => state.ErrorMessage ?? "not yet");
        state.SelectedProducts.Should().BeEquivalentTo([Row(state, bakery.PandesalId), Row(state, bakery.MeringueId)]);

        state.OnlyAffected = true;
        state.SelectedProducts.Should().Equal(Row(state, bakery.MeringueId)!);
        state.PrintChoices(DateTime.Now).Select(c => c.Label).Should().Equal("Selected products (1)", "All products shown (2)");
    });

    [Test]
    public Task All_products_shown_are_the_tables_only_while_the_screen_shows_it() => UiThread.RunAsync(async () =>
    {
        await AddBakeryAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        await Settle.UntilAsync(state, () => state.Products.Count == 3, () => $"{state.Products.Count} rows");
        IEnumerable<string> Printed() => state.PrintChoices(DateTime.Now).Single().Set.Products.Select(p => p.ProductName);

        // The screen's table, sorted the other way round.
        List<PriceSimulatorProductRow> sorted = [.. state.Products.Reverse()];
        var shown = state.ShowProductsFrom(() => sorted);
        Printed().Should().Equal(sorted.Select(r => r.ProductName));

        // The screen is left: the screen state lets go of its table, and prints its own list.
        shown.Dispose();
        Printed().Should().Equal(state.Products.Select(r => r.ProductName));
        state.ShownProducts().Should().BeSameAs(state.Products);
    });

    [Test]
    public Task A_new_price_works_out_the_change_and_the_other_way_round() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        var change = state.NewChange();
        state.Changes.Add(change);
        change.MaterialId = bakery.EggId;

        change.NewPrice = 0.0105m;
        change.ChangePercent.Should().Be(-25m);

        change.ChangePercent = 50m;
        change.NewPrice.Should().Be(0.021m);
    });

    [Test]
    public Task A_price_that_cant_be_is_refused_and_says_why() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();

        var flour = state.NewChange();
        state.Changes.Add(flour);
        flour.MaterialId = bakery.FlourId;
        flour.ChangePercent = 10m;

        await Settle.UntilAsync(state, () => state.ErrorMessage is not null, () => "no message");
        state.ErrorMessage.Should().Be($"Flour: {PriceChangeRules.NoPriceToChangeText}");

        flour.NewPrice = 0.06m;
        await Settle.UntilAsync(state, () => state.ErrorMessage is null && Row(state, bakery.PandesalId)?.IsAffected == true, () => state.ErrorMessage ?? "not yet");
    });

    [Test]
    public Task The_price_to_keep_markup_moves_the_current_price_with_the_base_cost() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Products.Single(p => p.Id == bakery.MeringueId).CurrentPrice = 5m;
            dbContext.Products.Single(p => p.Id == bakery.PandesalId).CurrentPrice = 3m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var state = await CreateLoadedStateAsync();
        AddChange(state, bakery.EggId, percent: 10m);
        await Settle.UntilAsync(state, () => Row(state, bakery.MeringueId)?.IsAffected == true, () => state.ErrorMessage ?? "not yet");

        var meringue = Row(state, bakery.MeringueId)!;
        meringue.CurrentPrice.Should().Be(5m);
        meringue.PriceToKeepMarkup.Should().NotBeNull().And.BeApproximately(5.5m, 0.0000001m, "its base cost is 10% up with the egg");
        meringue.SimulatedMarkupPercent.Should().BeLessThan(meringue.MarkupPercent!.Value);
        Row(state, bakery.PandesalId)!.PriceToKeepMarkup.Should().Be(3m, "no change reaches it");
        Row(state, bakery.BoxId)!.PriceToKeepMarkup.Should().BeNull("it has no current price");
    });

    [Test]
    public Task A_saved_scenario_opens_again_its_percentage_on_the_price_then_and_its_new_price_as_saved() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = await CreateLoadedStateAsync();
        AddChange(state, bakery.EggId, percent: 10m);
        AddChange(state, bakery.FlourId, newPrice: 0.06m);
        state.HasUnsavedChanges.Should().BeTrue();

        await state.SaveScenarioCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Saved the scenario Supplier A.");
        var saved = state.Scenarios.Should().ContainSingle().Subject;
        state.SelectedScenario.Should().BeSameAs(saved);
        state.HasUnsavedChanges.Should().BeFalse();

        // The egg's price moves, then the app is opened again.
        await SetPriceAsync(bakery.EggId, null, 0.02m);
        var later = await CreateLoadedStateAsync();
        later.Changes.Should().BeEmpty();
        later.SelectedScenario.Should().BeNull();

        await later.OpenScenarioCommand.ExecuteAsync(later.Scenarios.Single());

        later.SelectedScenario.Should().BeSameAs(later.Scenarios.Single());
        later.Changes.Select(c => (c.MaterialId, c.CurrentPrice, c.ChangePercent, c.NewPrice)).Should().Equal(
            (bakery.EggId, 0.02m, 10m, 0.022m),
            (bakery.FlourId, (decimal?)null, (decimal?)null, 0.06m));
        later.HasUnsavedChanges.Should().BeFalse();
        await Settle.UntilAsync(later, () => Row(later, bakery.PandesalId)?.IsAffected == true, () => later.ErrorMessage ?? "not yet");
    });

    [Test]
    public Task Opening_a_scenario_over_changes_not_saved_asks_and_keeping_them_leaves_the_table_as_it_was() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var keep = true;
        string? asked = null;
        var state = await CreateLoadedStateAsync(confirm: question =>
        {
            asked = question;
            return !keep;
        });
        AddChange(state, bakery.EggId, percent: 10m);
        await state.SaveScenarioCommand.ExecuteAsync();
        var scenario = state.Scenarios.Single();
        _ = state.ClearCommand.ExecuteAsync();
        state.SelectedScenario.Should().BeNull("Clear All closes the scenario");
        AddChange(state, bakery.FlourId, newPrice: 0.06m);

        await state.OpenScenarioCommand.ExecuteAsync(scenario);

        asked.Should().Be("Open the scenario Supplier A? The price changes in the table aren't saved, and it replaces them.");
        state.Changes.Select(c => c.MaterialId).Should().Equal(bakery.FlourId);
        state.SelectedScenario.Should().BeNull();

        keep = false;
        await state.OpenScenarioCommand.ExecuteAsync(scenario);

        state.Changes.Select(c => (c.MaterialId, c.ChangePercent)).Should().Equal((bakery.EggId, (decimal?)10m));
        state.SelectedScenario.Should().BeSameAs(scenario);
    });

    [Test]
    public Task Saving_under_another_scenarios_name_asks_before_replacing_its_changes() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var replace = false;
        string? asked = null;
        var state = await CreateLoadedStateAsync(name: "supplier a", confirm: question =>
        {
            asked = question;
            return replace;
        });
        AddChange(state, bakery.EggId, percent: 10m);
        await state.SaveScenarioCommand.ExecuteAsync();
        _ = state.ClearCommand.ExecuteAsync();
        AddChange(state, bakery.EggId, percent: 20m);

        await state.SaveScenarioCommand.ExecuteAsync();

        asked.Should().Be("Replace the price changes saved as supplier a with the ones in the table?");
        (await new PriceScenarioRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Single().Lines.Single().ChangePercent.Should().Be(10m);

        replace = true;
        await state.SaveScenarioCommand.ExecuteAsync();

        var saved = (await new PriceScenarioRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        saved.Lines.Single().ChangePercent.Should().Be(20m);
        state.SelectedScenario!.Id.Should().Be(saved.Id);
    });

    [Test]
    public Task Deleting_the_open_scenario_keeps_its_changes_in_the_table_and_an_empty_table_cant_be_saved() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var state = await CreateLoadedStateAsync();
        await state.SaveScenarioCommand.ExecuteAsync();
        state.ErrorMessage.Should().Be(PriceScenarioRules.NoChangesText);
        state.Scenarios.Should().BeEmpty();

        AddChange(state, bakery.EggId, percent: 10m);
        await state.SaveScenarioCommand.ExecuteAsync();
        await state.DeleteScenarioCommand.ExecuteAsync();

        state.SuccessMessage.Should().Be("Deleted the scenario Supplier A.");
        state.Scenarios.Should().BeEmpty();
        state.SelectedScenario.Should().BeNull();
        state.Changes.Should().ContainSingle();
        state.HasUnsavedChanges.Should().BeTrue("no scenario holds them now");
        (await new PriceScenarioRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Should().BeEmpty();
    });

    [Test]
    public Task Unbuffered_prices_every_figure_and_the_report_at_true_costs_and_Buffered_switches_back() => UiThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        await SetPriceAsync(bakery.FlourId, null, 0.05m);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterials.Single(m => m.Id == bakery.FlourId).BufferedCostPerBaseUnit = 0.06m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var state = await CreateLoadedStateAsync();
        AddChange(state, bakery.FlourId, percent: 10m);
        await Settle.UntilAsync(state, () => Row(state, bakery.PandesalId)?.IsAffected == true, () => state.ErrorMessage ?? "not yet");
        var buffered = Row(state, bakery.PandesalId)!;
        state.CostBasisButtonLabel.Should().Be("Unbuffered");
        state.IsUnbuffered.Should().BeFalse();

        await state.ToggleCostBasisCommand.ExecuteAsync();

        state.IsUnbuffered.Should().BeTrue();
        state.CostBasisButtonLabel.Should().Be("Buffered");
        await Settle.UntilAsync(state, () => !ReferenceEquals(Row(state, bakery.PandesalId), buffered), () => "still buffered");
        var unbuffered = Row(state, bakery.PandesalId)!;
        unbuffered.RawMaterialCost.Should().BeApproximately(buffered.RawMaterialCost * 0.05m / 0.06m, 0.0000001m, "flour at 0.05 a gram, not its buffered 0.06");
        unbuffered.BaseCost.Should().BeApproximately(buffered.BaseCost * 0.05m / 0.06m, 0.0000001m);
        unbuffered.SimulatedBaseCost.Should().BeApproximately(unbuffered.BaseCost * 1.1m, 0.0000001m, "the change moves the true cost by the same 10%");
        Row(state, bakery.MeringueId)!.BaseCost.Should().Be(Row(state, bakery.MeringueId)!.SimulatedBaseCost, "no change reaches it");

        var pandesal = state.PrintChoices(DateTime.Now).Single().Set.Products.Single(p => p.ProductName == "Pandesal");
        pandesal.CostBasis.Should().Be(CostBasis.Unbuffered, "the page says it's unbuffered");
        pandesal.Costs.BaseCost.Should().Be(unbuffered.SimulatedBaseCost);
        pandesal.Simulation!.Now.BaseCost.Should().Be(unbuffered.BaseCost);

        await state.ToggleCostBasisCommand.ExecuteAsync();

        await Settle.UntilAsync(state, () => Row(state, bakery.PandesalId)?.BaseCost == buffered.BaseCost, () => "still unbuffered");
        Row(state, bakery.PandesalId)!.SimulatedBaseCost.Should().Be(buffered.SimulatedBaseCost);
        state.HasUnsavedChanges.Should().BeTrue("the switch is no change to the scenario");
    });

    [Test]
    public Task Export_while_unbuffered_suggests_a_workbook_that_says_so() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        string? asked = null;
        state.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(null);
        });

        await state.ToggleCostBasisCommand.ExecuteAsync();
        await state.ExportCommand.ExecuteAsync();

        asked.Should().Be("Price Simulator (Unbuffered)");
    });

    [Test]
    public Task A_double_clicked_product_asks_to_be_opened() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        List<int> opened = [];
        using var subscription = state.ProductOpenRequests.Subscribe(opened.Add);

        await state.OpenProductCommand.ExecuteAsync(42);

        opened.Should().Equal(42);
    });

    [Test]
    public Task Export_asks_for_the_Price_Simulator_workbook() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        string? asked = null;
        state.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(@"C:\Reports\Price Simulator 2026-10-04.xlsx");
        });

        await state.ExportCommand.ExecuteAsync();

        asked.Should().Be(PriceSimulatorState.ExportTitle);
        state.SuccessMessage.Should().Be(@"Saved Price Simulator 2026-10-04.xlsx in C:\Reports.");
    });

    private static PriceSimulatorProductRow? Row(PriceSimulatorState state, int productId) => state.Products.FirstOrDefault(r => r.ProductId == productId);

    private PriceSimulatorState CreateState()
    {
        var factory = CreateDbContextFactory();
        return new PriceSimulatorState(
            new ProductRepository(factory),
            new RawMaterialRepository(factory),
            new IngredientBomLineRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new PriceScenarioRepository(factory),
            SavedProducts(factory),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>A screen loaded, answering Save Scenario's name with <paramref name="name"/> and every question with <paramref name="confirm"/>.</summary>
    private async Task<PriceSimulatorState> CreateLoadedStateAsync(string? name = "Supplier A", Func<string, bool>? confirm = null)
    {
        var state = CreateState();
        state.PromptForName.RegisterHandler(interaction => interaction.SetOutput(name));
        state.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(confirm?.Invoke(interaction.Input) ?? true));
        await state.LoadCommand.ExecuteAsync();
        return state;
    }

    /// <summary>A row added to the Price Changes table for <paramref name="materialId"/>, as the add row does, then typed.</summary>
    private static SimulatedPriceChange AddChange(PriceSimulatorState state, int materialId, decimal? percent = null, decimal? newPrice = null)
    {
        var change = state.NewChange();
        state.Changes.Add(change);
        change.MaterialId = materialId;
        if (percent is { } p)
        {
            change.ChangePercent = p;
        }

        if (newPrice is { } price)
        {
            change.NewPrice = price;
        }

        return change;
    }

    private ReportsState CreateReportsState()
    {
        var factory = CreateDbContextFactory();
        return new ReportsState(
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialRepository(factory),
            new ProductRepository(factory),
            SavedProducts(factory),
            new LoggerConfiguration().CreateLogger());
    }

    private SavedProductReader SavedProducts(Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.TinapayanRMSDbContext> factory) => new(
        new PackagingLinkRepository(factory),
        new ProductSubAssemblyUsageRepository(factory),
        new PackagingLineRepository(factory),
        new CostingParametersRepository(factory),
        new SubAssemblyLineRepository(factory),
        new ProductRepository(factory),
        new CostingDefaultsRepository(factory),
        CreateUnitOfWork());
}
