using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>The costing a new product starts with, set in Settings: one row, all zero until first saved.</summary>
[Collection(DatabaseCollection.Name)]
public class CostingDefaultsPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private CostingDefaultsRepository CreateRepository() => new(CreateDbContextFactory());

    private static CostingDefaults Defaults(decimal labor, decimal overhead, decimal administrative, decimal margin, decimal vat) =>
        new() { LaborPercent = labor, OverheadPercent = overhead, AdministrativePercent = administrative, MarginPercent = margin, VatPercent = vat };

    [Fact]
    public async Task GetAsync_reads_all_zero_before_anything_is_saved()
    {
        var defaults = await CreateRepository().GetAsync(Cancellation);

        new[] { defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent, defaults.VatPercent }
            .Should().OnlyContain(p => p == 0m);
    }

    [Fact]
    public async Task SetAsync_saves_one_row_and_a_second_save_updates_it()
    {
        await CreateRepository().SetAsync(Defaults(25m, 10m, 5m, 40m, 12m), Cancellation);
        await CreateRepository().SetAsync(Defaults(30m, 10m, 5m, 45m, 12m), Cancellation);

        var defaults = await CreateRepository().GetAsync(Cancellation);
        (defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent, defaults.VatPercent)
            .Should().Be((30m, 10m, 5m, 45m, 12m));
        await using var dbContext = CreateDbContext();
        (await dbContext.CostingDefaults.CountAsync(Cancellation)).Should().Be(1);
    }

    [Fact]
    public async Task SetAsync_refuses_a_negative_percentage()
    {
        var act = () => CreateRepository().SetAsync(Defaults(-1m, 0m, 0m, 0m, 0m), Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_table_holds_one_row_at_most()
    {
        await using var dbContext = CreateDbContext();
        dbContext.CostingDefaults.Add(new CostingDefaults { Id = 2 });

        var act = () => dbContext.SaveChangesAsync(Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>("CK_CostingDefaults_SingleRow");
    }
}
