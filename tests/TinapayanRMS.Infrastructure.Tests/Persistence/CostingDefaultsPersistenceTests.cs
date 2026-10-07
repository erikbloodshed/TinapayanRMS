using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// The stage and costing a new product, the shrinkage a new section and the cost buffer a new raw material start with,
/// set in Settings: one row, reading as new products in Production, the costing at zero, the shrinkage at 3% and the
/// buffer at 10% until first saved.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CostingDefaultsPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private CostingDefaultsRepository CreateRepository() => new(CreateDbContextFactory());

    private static CostingDefaults Defaults(decimal labor, decimal overhead, decimal administrative, decimal margin, decimal vat, decimal shrinkage = 3m) =>
        new() { LaborPercent = labor, OverheadPercent = overhead, AdministrativePercent = administrative, MarginPercent = margin, VatPercent = vat, ShrinkagePercent = shrinkage };

    [Fact]
    public async Task GetAsync_reads_the_costing_at_zero_and_the_shrinkage_at_3_percent_before_anything_is_saved()
    {
        var defaults = await CreateRepository().GetAsync(Cancellation);

        new[] { defaults.LaborPercent, defaults.OverheadPercent, defaults.AdministrativePercent, defaults.MarginPercent, defaults.VatPercent }
            .Should().OnlyContain(p => p == 0m);
        defaults.ShrinkagePercent.Should().Be(3m);
        defaults.NewProductStage.Should().Be(ProductStage.Production);
        defaults.CostBufferPercent.Should().Be(10m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public async Task SetAsync_keeps_the_cost_buffer_typed_even_none(int buffer)
    {
        await CreateRepository().SetAsync(new CostingDefaults { CostBufferPercent = buffer }, Cancellation);

        (await CreateRepository().GetAsync(Cancellation)).CostBufferPercent.Should().Be(buffer, "a buffer of 0 isn't left for the store default to make 10%");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task SetAsync_refuses_a_cost_buffer_a_material_couldnt_have(int buffer)
    {
        var act = () => CreateRepository().SetAsync(new CostingDefaults { CostBufferPercent = buffer }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetAsync_keeps_the_stage_picked_for_new_products_even_Research_and_Development()
    {
        await CreateRepository().SetAsync(new CostingDefaults { NewProductStage = ProductStage.ResearchAndDevelopment }, Cancellation);
        (await CreateRepository().GetAsync(Cancellation)).NewProductStage.Should().Be(
            ProductStage.ResearchAndDevelopment, "the CLR default isn't left for the store default to make Production");

        await CreateRepository().SetAsync(new CostingDefaults { NewProductStage = ProductStage.Production }, Cancellation);
        (await CreateRepository().GetAsync(Cancellation)).NewProductStage.Should().Be(ProductStage.Production);
    }

    [Fact]
    public async Task SetAsync_keeps_the_shrinkage_typed_even_a_first_save_of_none()
    {
        await CreateRepository().SetAsync(Defaults(0m, 0m, 0m, 0m, 0m, shrinkage: 0m), Cancellation);
        (await CreateRepository().GetAsync(Cancellation)).ShrinkagePercent.Should().Be(0m, "the column has no store default to put 3% in its place");

        await CreateRepository().SetAsync(Defaults(0m, 0m, 0m, 0m, 0m, shrinkage: 5.5m), Cancellation);
        (await CreateRepository().GetAsync(Cancellation)).ShrinkagePercent.Should().Be(5.5m);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(100)]
    public async Task SetAsync_refuses_a_shrinkage_a_section_couldnt_have(int shrinkage)
    {
        var act = () => CreateRepository().SetAsync(Defaults(0m, 0m, 0m, 0m, 0m, shrinkage: shrinkage), Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
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
