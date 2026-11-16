using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Settings;

namespace TinapayanRMS.Presentation.Tests.Settings;

/// <summary>Settings' Costing Defaults tab: loads what's saved, saves what's typed and picked, and refuses a negative percentage.</summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class CostingDefaultsViewModelTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private CostingDefaultsViewModel CreateViewModel()
    {
        return new CostingDefaultsViewModel(
            new CostingDefaultsRepository(CreateDbContextFactory()),
            new CostingParametersValidator(),
            new CostRecalculator(CreateDbContextFactory()),
            new LoggerConfiguration().CreateLogger());
    }

    [Test]
    public Task Save_keeps_the_percentages_and_Load_reads_them_back() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync();
        viewModel.EditingLaborPercent = 25m;
        viewModel.EditingOverheadPercent = 10m;
        viewModel.EditingAdministrativePercent = 5m;
        viewModel.EditingMarginPercent = 40m;
        viewModel.EditingVatPercent = 12m;
        viewModel.EditingWastagePercent = 4.5m;
        viewModel.EditingNewProductStage = ProductStage.Innovation;
        viewModel.EditingCostBufferPercent = 8m;
        viewModel.EditingPriceRounding = viewModel.PriceRoundingOptions.Single(o => o.Step == 0.25m);

        await viewModel.SaveCommand.ExecuteAsync();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().NotBeNull();
        var reloaded = CreateViewModel();
        await reloaded.LoadCommand.ExecuteAsync();
        (reloaded.EditingLaborPercent, reloaded.EditingOverheadPercent, reloaded.EditingAdministrativePercent, reloaded.EditingMarginPercent, reloaded.EditingVatPercent)
            .Should().Be((25m, 10m, 5m, 40m, 12m));
        reloaded.EditingWastagePercent.Should().Be(4.5m);
        reloaded.EditingNewProductStage.Should().Be(ProductStage.Innovation);
        reloaded.EditingCostBufferPercent.Should().Be(8m);
        reloaded.EditingPriceRounding.Should().Be(new PriceRoundingOption(0.25m, "Up to the next 0.25"));
        reloaded.PriceRoundingOptions[0].Label.Should().Be("None", "rounding is off until it's chosen");
    });

    [Test]
    public Task Load_shows_a_cost_buffer_of_10_percent_before_anything_is_saved_and_Save_refuses_one_over_100() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.EditingCostBufferPercent = 50m;

        await viewModel.LoadCommand.ExecuteAsync();
        viewModel.EditingCostBufferPercent.Should().Be(10m);

        viewModel.EditingCostBufferPercent = 101m;
        await viewModel.SaveCommand.ExecuteAsync();

        viewModel.ErrorMessage.Should().Be("Cost buffer must be at least 0 and at most 100 percent.");
        (await new CostingDefaultsRepository(CreateDbContextFactory()).GetAsync(Cancellation)).CostBufferPercent.Should().Be(10m);
    });

    [Test]
    public Task Load_files_new_products_in_Production_before_anything_is_saved_and_offers_both_stages() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.EditingNewProductStage = ProductStage.Innovation;

        await viewModel.LoadCommand.ExecuteAsync();

        viewModel.EditingNewProductStage.Should().Be(ProductStage.Production);
        viewModel.Stages.Select(s => s.Value).Should().Equal("Production", "Innovation");
    });

    [Test]
    public Task Load_shows_a_wastage_of_3_percent_before_anything_is_saved() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        viewModel.EditingWastagePercent = 9m;

        await viewModel.LoadCommand.ExecuteAsync();

        viewModel.EditingWastagePercent.Should().Be(3m);
    });

    [Test]
    public Task Save_refuses_a_wastage_over_100_percent_and_saves_nothing() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync();
        viewModel.EditingWastagePercent = 101m;

        await viewModel.SaveCommand.ExecuteAsync();

        viewModel.ErrorMessage.Should().Be("The wastage must be from 0% to 100%.");
        (await new CostingDefaultsRepository(CreateDbContextFactory()).GetAsync(Cancellation)).WastagePercent.Should().Be(3m);
    });

    [Test]
    public Task Save_refuses_a_negative_percentage_and_saves_nothing() => UiThread.RunAsync(async () =>
    {
        var viewModel = CreateViewModel();
        await viewModel.LoadCommand.ExecuteAsync();
        viewModel.EditingVatPercent = -12m;

        await viewModel.SaveCommand.ExecuteAsync();

        viewModel.ErrorMessage.Should().Be("The VAT percentage can't be negative.");
        (await new CostingDefaultsRepository(CreateDbContextFactory()).GetAsync(Cancellation)).VatPercent.Should().Be(0m);
    });

    [Test]
    public Task Recalculate_costs_puts_right_a_cost_changed_outside_the_app_and_says_how_many() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var syrupId = await AddMadeIngredientAsync("Syrup", [(flourId, 1000m)]);
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(m => m.Id == syrupId)
                .ExecuteUpdateAsync(set => set.SetProperty(m => m.CostPerBaseUnit, 9m).SetProperty(m => m.BufferedCostPerBaseUnit, 9m), Cancellation);
        }

        var viewModel = CreateViewModel();
        await viewModel.RecalculateCostsCommand.ExecuteAsync();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Be("Costs recalculated: 1 made or linked ingredient was out of date, and is right now.");
        await using var check = CreateDbContext();
        (await check.RawMaterials.SingleAsync(m => m.Id == syrupId, Cancellation)).BufferedCostPerBaseUnit
            .Should().Be(0.05m, "₱50 of flour over the 1,000 g it makes");
    });

    [Test]
    [Arguments(0, "Costs recalculated: every one was already up to date.")]
    [Arguments(1, "Costs recalculated: 1 made or linked ingredient was out of date, and is right now.")]
    [Arguments(2, "Costs recalculated: 2 made or linked ingredients were out of date, and are right now.")]
    public void RecalculatedText_says_how_many_were_out_of_date(int ingredients, string expected)
    {
        CostingDefaultsViewModel.RecalculatedText(new CostRecalculation(ingredients)).Should().Be(expected);
    }
}
