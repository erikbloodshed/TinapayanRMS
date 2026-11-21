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
using TinapayanRMS.UseCases.Settings;

namespace TinapayanRMS.Presentation.Tests.Settings;

/// <summary>Settings' Costing Defaults tab: loads what's saved, saves what's typed and picked, and refuses a negative percentage.</summary>
[NotInParallel(DatabaseCollection.A)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.A)]
public class CostingDefaultsStateTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private CostingDefaultsState CreateState()
    {
        return new CostingDefaultsState(
            new CostingDefaultsEditor(
                new CostingDefaultsRepository(CreateDbContextFactory()),
                new CostingParametersValidator(),
                new CostRecalculator(CreateDbContextFactory())),
            new LoggerConfiguration().CreateLogger());
    }

    [Test]
    public Task Save_keeps_the_percentages_and_Load_reads_them_back() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.EditingLaborPercent = 25m;
        state.EditingOverheadPercent = 10m;
        state.EditingAdministrativePercent = 5m;
        state.EditingMarkupPercent = 40m;
        state.EditingVatPercent = 12m;
        state.EditingWastagePercent = 4.5m;
        state.EditingNewProductStage = ProductStage.Innovation;
        state.EditingCostBufferPercent = 8m;
        state.EditingPriceRounding = state.PriceRoundingOptions.Single(o => o.Step == 0.25m);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().NotBeNull();
        var reloaded = CreateState();
        await reloaded.LoadCommand.ExecuteAsync();
        (reloaded.EditingLaborPercent, reloaded.EditingOverheadPercent, reloaded.EditingAdministrativePercent, reloaded.EditingMarkupPercent, reloaded.EditingVatPercent)
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
        var state = CreateState();
        state.EditingCostBufferPercent = 50m;

        await state.LoadCommand.ExecuteAsync();
        state.EditingCostBufferPercent.Should().Be(10m);

        state.EditingCostBufferPercent = 101m;
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("Cost buffer must be at least 0 and at most 100 percent.");
        (await new CostingDefaultsRepository(CreateDbContextFactory()).GetAsync(Cancellation)).CostBufferPercent.Should().Be(10m);
    });

    [Test]
    public Task Load_files_new_products_in_Production_before_anything_is_saved_and_offers_both_stages() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        state.EditingNewProductStage = ProductStage.Innovation;

        await state.LoadCommand.ExecuteAsync();

        state.EditingNewProductStage.Should().Be(ProductStage.Production);
        state.Stages.Select(s => s.Value).Should().Equal("Production", "Innovation");
    });

    [Test]
    public Task Load_shows_a_wastage_of_3_percent_before_anything_is_saved() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        state.EditingWastagePercent = 9m;

        await state.LoadCommand.ExecuteAsync();

        state.EditingWastagePercent.Should().Be(3m);
    });

    [Test]
    public Task Save_refuses_a_wastage_over_100_percent_and_saves_nothing() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.EditingWastagePercent = 101m;

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("The wastage must be from 0% to 100%.");
        (await new CostingDefaultsRepository(CreateDbContextFactory()).GetAsync(Cancellation)).WastagePercent.Should().Be(3m);
    });

    [Test]
    public Task Save_refuses_a_negative_percentage_and_saves_nothing() => UiThread.RunAsync(async () =>
    {
        var state = CreateState();
        await state.LoadCommand.ExecuteAsync();
        state.EditingVatPercent = -12m;

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("The VAT percentage can't be negative.");
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

        var state = CreateState();
        await state.RecalculateCostsCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Be("Costs recalculated: 1 made or linked ingredient was out of date, and is right now.");
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
        CostingDefaultsState.RecalculatedText(new CostRecalculation(ingredients)).Should().Be(expected);
    }
}
