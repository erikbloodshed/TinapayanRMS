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
    public Task With_no_changes_every_product_prices_as_Reports_does() => StaThread.RunAsync(async () =>
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
    });

    [Fact]
    public Task A_rise_in_egg_reaches_the_linked_egg_white_its_products_and_the_composites_holding_them() => StaThread.RunAsync(async () =>
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
    });

    [Fact]
    public Task The_report_sets_each_product_beside_today_with_the_changes_that_reach_it() => StaThread.RunAsync(async () =>
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
    });

    [Fact]
    public Task Picked_products_print_on_their_own_in_the_tables_order_and_stay_picked_through_a_change() => StaThread.RunAsync(async () =>
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
    });

    [Fact]
    public Task A_new_price_works_out_the_change_and_the_other_way_round() => StaThread.RunAsync(async () =>
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
    });

    [Fact]
    public Task A_price_that_cant_be_is_refused_and_says_why() => StaThread.RunAsync(async () =>
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
    });

    [Fact]
    public Task The_price_to_keep_markup_moves_the_current_price_with_the_base_cost() => StaThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Products.Single(p => p.Id == bakery.MeringueId).CurrentPrice = 5m;
            dbContext.Products.Single(p => p.Id == bakery.PandesalId).CurrentPrice = 3m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var viewModel = await CreateLoadedViewModelAsync();
        AddChange(viewModel, bakery.EggId, percent: 10m);
        await WaitUntilSettledAsync(() => Row(viewModel, bakery.MeringueId)?.IsAffected == true, () => viewModel.ErrorMessage ?? "not yet");

        var meringue = Row(viewModel, bakery.MeringueId)!;
        meringue.CurrentPrice.Should().Be(5m);
        meringue.PriceToKeepMarkup.Should().NotBeNull().And.BeApproximately(5.5m, 0.0000001m, "its base cost is 10% up with the egg");
        meringue.SimulatedMarkupPercent.Should().BeLessThan(meringue.MarkupPercent!.Value);
        Row(viewModel, bakery.PandesalId)!.PriceToKeepMarkup.Should().Be(3m, "no change reaches it");
        Row(viewModel, bakery.BoxId)!.PriceToKeepMarkup.Should().BeNull("it has no current price");
    });

    [Fact]
    public Task A_saved_scenario_opens_again_its_percentage_on_the_price_then_and_its_new_price_as_saved() => StaThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var viewModel = await CreateLoadedViewModelAsync();
        AddChange(viewModel, bakery.EggId, percent: 10m);
        AddChange(viewModel, bakery.FlourId, newPrice: 0.06m);
        viewModel.HasUnsavedChanges.Should().BeTrue();

        await viewModel.SaveScenarioCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Saved the scenario Supplier A.");
        var saved = viewModel.Scenarios.Should().ContainSingle().Subject;
        viewModel.SelectedScenario.Should().BeSameAs(saved);
        viewModel.HasUnsavedChanges.Should().BeFalse();

        // The egg's price moves, then the app is opened again.
        await SetPriceAsync(bakery.EggId, null, 0.02m);
        var later = await CreateLoadedViewModelAsync();
        later.Changes.Should().BeEmpty();
        later.SelectedScenario.Should().BeNull();

        await later.OpenScenarioCommand.Execute(later.Scenarios.Single());

        later.SelectedScenario.Should().BeSameAs(later.Scenarios.Single());
        later.Changes.Select(c => (c.MaterialId, c.CurrentPrice, c.ChangePercent, c.NewPrice)).Should().Equal(
            (bakery.EggId, 0.02m, 10m, 0.022m),
            (bakery.FlourId, (decimal?)null, (decimal?)null, 0.06m));
        later.HasUnsavedChanges.Should().BeFalse();
        await WaitUntilSettledAsync(() => Row(later, bakery.PandesalId)?.IsAffected == true, () => later.ErrorMessage ?? "not yet");
    });

    [Fact]
    public Task Opening_a_scenario_over_changes_not_saved_asks_and_keeping_them_leaves_the_table_as_it_was() => StaThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var keep = true;
        string? asked = null;
        var viewModel = await CreateLoadedViewModelAsync(confirm: question =>
        {
            asked = question;
            return !keep;
        });
        AddChange(viewModel, bakery.EggId, percent: 10m);
        await viewModel.SaveScenarioCommand.Execute();
        var scenario = viewModel.Scenarios.Single();
        viewModel.ClearCommand.Execute().Subscribe();
        viewModel.SelectedScenario.Should().BeNull("Clear All closes the scenario");
        AddChange(viewModel, bakery.FlourId, newPrice: 0.06m);

        await viewModel.OpenScenarioCommand.Execute(scenario);

        asked.Should().Be("Open the scenario Supplier A? The price changes in the table aren't saved, and it replaces them.");
        viewModel.Changes.Select(c => c.MaterialId).Should().Equal(bakery.FlourId);
        viewModel.SelectedScenario.Should().BeNull();

        keep = false;
        await viewModel.OpenScenarioCommand.Execute(scenario);

        viewModel.Changes.Select(c => (c.MaterialId, c.ChangePercent)).Should().Equal((bakery.EggId, (decimal?)10m));
        viewModel.SelectedScenario.Should().BeSameAs(scenario);
    });

    [Fact]
    public Task Saving_under_another_scenarios_name_asks_before_replacing_its_changes() => StaThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var replace = false;
        string? asked = null;
        var viewModel = await CreateLoadedViewModelAsync(name: "supplier a", confirm: question =>
        {
            asked = question;
            return replace;
        });
        AddChange(viewModel, bakery.EggId, percent: 10m);
        await viewModel.SaveScenarioCommand.Execute();
        viewModel.ClearCommand.Execute().Subscribe();
        AddChange(viewModel, bakery.EggId, percent: 20m);

        await viewModel.SaveScenarioCommand.Execute();

        asked.Should().Be("Replace the price changes saved as supplier a with the ones in the table?");
        (await new PriceScenarioRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Single().Lines.Single().ChangePercent.Should().Be(10m);

        replace = true;
        await viewModel.SaveScenarioCommand.Execute();

        var saved = (await new PriceScenarioRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        saved.Lines.Single().ChangePercent.Should().Be(20m);
        viewModel.SelectedScenario!.Id.Should().Be(saved.Id);
    });

    [Fact]
    public Task Deleting_the_open_scenario_keeps_its_changes_in_the_table_and_an_empty_table_cant_be_saved() => StaThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        var viewModel = await CreateLoadedViewModelAsync();
        await viewModel.SaveScenarioCommand.Execute();
        viewModel.ErrorMessage.Should().Be(PriceScenarioRules.NoChangesText);
        viewModel.Scenarios.Should().BeEmpty();

        AddChange(viewModel, bakery.EggId, percent: 10m);
        await viewModel.SaveScenarioCommand.Execute();
        await viewModel.DeleteScenarioCommand.Execute();

        viewModel.SuccessMessage.Should().Be("Deleted the scenario Supplier A.");
        viewModel.Scenarios.Should().BeEmpty();
        viewModel.SelectedScenario.Should().BeNull();
        viewModel.Changes.Should().ContainSingle();
        viewModel.HasUnsavedChanges.Should().BeTrue("no scenario holds them now");
        (await new PriceScenarioRepository(CreateDbContextFactory()).GetAllAsync(Cancellation)).Should().BeEmpty();
    });

    [Fact]
    public Task Unbuffered_prices_every_figure_and_the_report_at_true_costs_and_Buffered_switches_back() => StaThread.RunAsync(async () =>
    {
        var bakery = await AddBakeryAsync();
        await SetPriceAsync(bakery.FlourId, null, 0.05m);
        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterials.Single(m => m.Id == bakery.FlourId).BufferedCostPerBaseUnit = 0.06m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var viewModel = await CreateLoadedViewModelAsync();
        AddChange(viewModel, bakery.FlourId, percent: 10m);
        await WaitUntilSettledAsync(() => Row(viewModel, bakery.PandesalId)?.IsAffected == true, () => viewModel.ErrorMessage ?? "not yet");
        var buffered = Row(viewModel, bakery.PandesalId)!;
        viewModel.CostBasisButtonLabel.Should().Be("Unbuffered");
        viewModel.IsUnbuffered.Should().BeFalse();

        await viewModel.ToggleCostBasisCommand.Execute();

        viewModel.IsUnbuffered.Should().BeTrue();
        viewModel.CostBasisButtonLabel.Should().Be("Buffered");
        await WaitUntilSettledAsync(() => !ReferenceEquals(Row(viewModel, bakery.PandesalId), buffered), () => "still buffered");
        var unbuffered = Row(viewModel, bakery.PandesalId)!;
        unbuffered.RawMaterialCost.Should().BeApproximately(buffered.RawMaterialCost * 0.05m / 0.06m, 0.0000001m, "flour at 0.05 a gram, not its buffered 0.06");
        unbuffered.BaseCost.Should().BeApproximately(buffered.BaseCost * 0.05m / 0.06m, 0.0000001m);
        unbuffered.SimulatedBaseCost.Should().BeApproximately(unbuffered.BaseCost * 1.1m, 0.0000001m, "the change moves the true cost by the same 10%");
        Row(viewModel, bakery.MeringueId)!.BaseCost.Should().Be(Row(viewModel, bakery.MeringueId)!.SimulatedBaseCost, "no change reaches it");

        var pandesal = viewModel.PrintChoices(DateTime.Now).Single().Set.Products.Single(p => p.ProductName == "Pandesal");
        pandesal.CostBasis.Should().Be(CostBasis.Unbuffered, "the page says it's unbuffered");
        pandesal.Costs.BaseCost.Should().Be(unbuffered.SimulatedBaseCost);
        pandesal.Simulation!.Now.BaseCost.Should().Be(unbuffered.BaseCost);

        await viewModel.ToggleCostBasisCommand.Execute();

        await WaitUntilSettledAsync(() => Row(viewModel, bakery.PandesalId)?.BaseCost == buffered.BaseCost, () => "still unbuffered");
        Row(viewModel, bakery.PandesalId)!.SimulatedBaseCost.Should().Be(buffered.SimulatedBaseCost);
        viewModel.HasUnsavedChanges.Should().BeTrue("the switch is no change to the scenario");
    });

    [Fact]
    public Task Export_while_unbuffered_suggests_a_workbook_that_says_so() => StaThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        string? asked = null;
        viewModel.ExportToExcel.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(null);
        });

        await viewModel.ToggleCostBasisCommand.Execute();
        await viewModel.ExportCommand.Execute();

        asked.Should().Be("Price Simulator (Unbuffered)");
    });

    [Fact]
    public Task A_double_clicked_product_asks_to_be_opened() => StaThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        List<int> opened = [];
        using var subscription = viewModel.ProductOpenRequests.Subscribe(opened.Add);

        await viewModel.OpenProductCommand.Execute(42);

        opened.Should().Equal(42);
    });

    [Fact]
    public Task Export_asks_for_the_Price_Simulator_workbook() => StaThread.RunAsync(async () =>
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
    });

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
            new PriceScenarioRepository(factory),
            SavedProducts(factory),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>A screen loaded, answering Save Scenario's name with <paramref name="name"/> and every question with <paramref name="confirm"/>.</summary>
    private async Task<PriceSimulatorViewModel> CreateLoadedViewModelAsync(string? name = "Supplier A", Func<string, bool>? confirm = null)
    {
        var viewModel = CreateViewModel();
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput(name));
        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(confirm?.Invoke(interaction.Input) ?? true));
        await viewModel.LoadCommand.Execute();
        return viewModel;
    }

    /// <summary>A row added to the Price Changes table for <paramref name="materialId"/>, as the add row does, then typed.</summary>
    private static SimulatedPriceChange AddChange(PriceSimulatorViewModel viewModel, int materialId, decimal? percent = null, decimal? newPrice = null)
    {
        var change = viewModel.NewChange();
        viewModel.Changes.Add(change);
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
        new ProductRepository(factory),
        new CostingDefaultsRepository(factory));

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
