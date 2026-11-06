using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// The Price Simulator's saved scenarios: a name and its price changes, each kept as typed. Saving under a name already
/// there replaces its changes, and deleting a material takes its changes out of every scenario.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class PriceScenarioPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly DateTime SavedAt = new(2026, 10, 22, 9, 14, 0);

    private PriceScenarioRepository CreateRepository() => new(CreateDbContextFactory());

    private static PriceScenarioLine Percent(int materialId, decimal percent) => new() { RawMaterialId = materialId, ChangePercent = percent };

    private static PriceScenarioLine Price(int materialId, decimal price) => new() { RawMaterialId = materialId, NewPrice = price };

    [Test]
    public async Task A_scenario_round_trips_with_its_lines_in_order_each_as_typed()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var eggId = await AddIngredientAsync("Egg", 0.014m);
        var repository = CreateRepository();

        var id = await repository.SaveAsync("  Supplier A, Nov  ", [Price(flourId, 1375m), Percent(eggId, 10m)], SavedAt, Cancellation);

        var scenario = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        scenario.Id.Should().Be(id);
        scenario.Name.Should().Be("Supplier A, Nov", "the name is saved trimmed");
        scenario.SavedAt.Should().Be(SavedAt);
        scenario.Lines.Select(l => (l.RawMaterialId, l.ChangePercent, l.NewPrice, l.SortOrder)).Should().Equal(
            (flourId, (decimal?)null, (decimal?)1375m, 0),
            (eggId, (decimal?)10m, (decimal?)null, 1));
    }

    [Test]
    public async Task Scenarios_read_newest_first_then_by_name_each_with_only_its_own_lines()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var eggId = await AddIngredientAsync("Egg", 0.014m);
        var repository = CreateRepository();
        await repository.SaveAsync("Worst case", [Percent(flourId, 20m), Percent(eggId, 15m)], SavedAt.AddDays(1), Cancellation);
        await repository.SaveAsync("Best case", [Percent(flourId, 2m)], SavedAt, Cancellation);
        await repository.SaveAsync("Average case", [Percent(eggId, 5m)], SavedAt, Cancellation);

        var scenarios = await repository.GetAllAsync(Cancellation);

        scenarios.Select(s => (s.Name, s.Lines.Count)).Should().Equal(("Worst case", 2), ("Average case", 1), ("Best case", 1));
    }

    [Test]
    public async Task Saving_under_a_name_already_there_ignoring_case_replaces_its_lines_and_keeps_its_name()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var eggId = await AddIngredientAsync("Egg", 0.014m);
        var repository = CreateRepository();
        var id = await repository.SaveAsync("Supplier A", [Percent(flourId, 10m), Percent(eggId, 5m)], SavedAt, Cancellation);

        var resavedId = await repository.SaveAsync("supplier a", [Price(eggId, 0.02m)], SavedAt.AddDays(1), Cancellation);

        resavedId.Should().Be(id);
        var scenario = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        scenario.Name.Should().Be("Supplier A");
        scenario.SavedAt.Should().Be(SavedAt.AddDays(1));
        scenario.Lines.Select(l => (l.RawMaterialId, l.NewPrice)).Should().Equal((eggId, (decimal?)0.02m));
        await using var dbContext = CreateDbContext();
        (await dbContext.PriceScenarioLines.CountAsync(Cancellation)).Should().Be(1, "the old lines are gone");
    }

    [Test]
    public async Task A_scenario_the_rules_refuse_writes_nothing()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var repository = CreateRepository();
        await repository.SaveAsync("Supplier A", [Percent(flourId, 10m)], SavedAt, Cancellation);

        var act = () => repository.SaveAsync("Supplier A", [Percent(flourId, -100m)], SavedAt, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(PriceChangeRules.FallTooFarText);
        (await repository.GetAllAsync(Cancellation)).Single().Lines.Single().ChangePercent.Should().Be(10m);
    }

    [Test]
    public async Task Deleting_a_scenario_takes_its_lines_and_leaves_the_others()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var repository = CreateRepository();
        var goneId = await repository.SaveAsync("Gone", [Percent(flourId, 10m)], SavedAt, Cancellation);
        await repository.SaveAsync("Kept", [Percent(flourId, 5m)], SavedAt, Cancellation);

        await repository.DeleteAsync(goneId, Cancellation);
        await repository.DeleteAsync(goneId, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Select(s => s.Name).Should().Equal("Kept");
        await using var dbContext = CreateDbContext();
        (await dbContext.PriceScenarioLines.CountAsync(Cancellation)).Should().Be(1);
    }

    [Test]
    public async Task Deleting_a_raw_material_takes_its_changes_out_of_every_scenario()
    {
        var flourId = await AddIngredientAsync("Flour", 0.05m);
        var eggId = await AddIngredientAsync("Egg", 0.014m);
        var repository = CreateRepository();
        await repository.SaveAsync("Supplier A", [Percent(flourId, 10m), Percent(eggId, 5m)], SavedAt, Cancellation);

        await new RawMaterialRepository(CreateDbContextFactory()).DeleteAsync(eggId, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Single().Lines.Select(l => l.RawMaterialId).Should().Equal(flourId);
    }
}
