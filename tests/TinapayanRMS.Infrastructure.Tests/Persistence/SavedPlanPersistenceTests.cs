using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// The Production Plan's saved plans: a name, its products and units in order, and its half batches. Saving under a name
/// already there replaces its lines, and deleting a product for good takes its lines out of every plan.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class SavedPlanPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly DateTime SavedAt = new(2026, 10, 24, 6, 30, 0);

    private SavedPlanRepository CreateRepository() => new(CreateDbContextFactory());

    private static SavedPlanLine Line(int productId, decimal units) => new() { ProductId = productId, Units = units };

    [Fact]
    public async Task A_plan_round_trips_with_its_lines_in_order_and_its_half_batches()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var repository = CreateRepository();

        var id = await repository.SaveAsync("  Saturday  ", [Line(pandesalId, 1150m), Line(ensaymadaId, 40.5m), Line(pandesalId, 50m)], true, SavedAt, Cancellation);

        var plan = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        plan.Id.Should().Be(id);
        plan.Name.Should().Be("Saturday", "the name is saved trimmed");
        plan.SavedAt.Should().Be(SavedAt);
        plan.HalfBatches.Should().BeTrue();
        plan.Lines.Select(l => (l.ProductId, l.Units, l.SortOrder)).Should().Equal(
            (pandesalId, 1150m, 0), (ensaymadaId, 40.5m, 1), (pandesalId, 50m, 2));
    }

    [Fact]
    public async Task Plans_read_newest_first_then_by_name_each_with_only_its_own_lines()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var repository = CreateRepository();
        await repository.SaveAsync("Weekday", [Line(pandesalId, 800m), Line(ensaymadaId, 20m)], false, SavedAt.AddDays(1), Cancellation);
        await repository.SaveAsync("Sunday", [Line(pandesalId, 600m)], false, SavedAt, Cancellation);
        await repository.SaveAsync("Saturday", [Line(ensaymadaId, 60m)], false, SavedAt, Cancellation);

        var plans = await repository.GetAllAsync(Cancellation);

        plans.Select(p => (p.Name, p.Lines.Count)).Should().Equal(("Weekday", 2), ("Saturday", 1), ("Sunday", 1));
    }

    [Fact]
    public async Task Saving_under_a_name_already_there_ignoring_case_replaces_its_lines_and_keeps_its_name()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var repository = CreateRepository();
        var id = await repository.SaveAsync("Saturday", [Line(pandesalId, 1000m), Line(ensaymadaId, 40m)], false, SavedAt, Cancellation);

        var resavedId = await repository.SaveAsync("saturday", [Line(ensaymadaId, 60m)], true, SavedAt.AddDays(7), Cancellation);

        resavedId.Should().Be(id);
        var plan = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        plan.Name.Should().Be("Saturday");
        plan.SavedAt.Should().Be(SavedAt.AddDays(7));
        plan.HalfBatches.Should().BeTrue();
        plan.Lines.Select(l => (l.ProductId, l.Units)).Should().Equal((ensaymadaId, 60m));
        await using var dbContext = CreateDbContext();
        (await dbContext.SavedPlanLines.CountAsync(Cancellation)).Should().Be(1, "the old lines are gone");
    }

    [Fact]
    public async Task A_plan_the_rules_refuse_writes_nothing()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var repository = CreateRepository();
        await repository.SaveAsync("Saturday", [Line(pandesalId, 1000m)], false, SavedAt, Cancellation);

        var act = () => repository.SaveAsync("Saturday", [Line(pandesalId, 0m)], false, SavedAt, Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>().WithMessage(SavedPlanRules.UnitsText);
        (await repository.GetAllAsync(Cancellation)).Single().Lines.Single().Units.Should().Be(1000m);
    }

    [Fact]
    public async Task Deleting_a_plan_takes_its_lines_and_leaves_the_others()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var repository = CreateRepository();
        var goneId = await repository.SaveAsync("Gone", [Line(pandesalId, 10m)], false, SavedAt, Cancellation);
        await repository.SaveAsync("Kept", [Line(pandesalId, 5m)], false, SavedAt, Cancellation);

        await repository.DeleteAsync(goneId, Cancellation);
        await repository.DeleteAsync(goneId, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Select(p => p.Name).Should().Equal("Kept");
        await using var dbContext = CreateDbContext();
        (await dbContext.SavedPlanLines.CountAsync(Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task Deleting_a_product_for_good_takes_its_lines_out_of_every_plan()
    {
        var pandesalId = await AddProductAsync("Pandesal");
        var ensaymadaId = await AddProductAsync("Ensaymada");
        var repository = CreateRepository();
        await repository.SaveAsync("Saturday", [Line(pandesalId, 1000m), Line(ensaymadaId, 40m)], false, SavedAt, Cancellation);
        var products = new ProductRepository(CreateDbContextFactory());

        await products.DeleteAsync(ensaymadaId, Cancellation);
        (await repository.GetAllAsync(Cancellation)).Single().Lines.Should().HaveCount(2, "a deleted product waits to be restored, so its line stays");
        await products.DeletePermanentlyAsync(ensaymadaId, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Single().Lines.Select(l => l.ProductId).Should().Equal(pandesalId);
    }
}
