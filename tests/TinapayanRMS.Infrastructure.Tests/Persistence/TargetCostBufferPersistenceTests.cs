using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A purchased material's target buffer, the one last typed for it, which IRawMaterialRepository.ReapplyTargetBuffersAsync puts
/// back on top of its dearest brand once rising prices have worn its fixed buffered cost down.
/// </summary>
[Collection(DatabaseCollection.C)]
public class TargetCostBufferPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    [Fact]
    public async Task Re_applying_the_target_puts_it_on_top_of_the_dearest_brand_and_reaches_the_recipes_using_it()
    {
        // Flour now costs 0.06 a gram, but its buffered cost stayed at 0.055 from an older price: under its 10% target.
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.06m, bufferedCostPerGram: 0.055m);
        await SetTargetAsync(flourId, 10m);
        var roleId = await AddRoleAsync("Filling");
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync("Pandesal", roleId, [(flourId, 1000m)]);
        (await SubAssemblyCostAsync(subAssemblyId)).Should().Be(55m);

        var changed = await CreateRepository().ReapplyTargetBuffersAsync([flourId], Cancellation);

        changed.Should().Be(1);
        await using var dbContext = CreateDbContext();
        var flour = await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == flourId, Cancellation);
        flour.BufferedCostPerBaseUnit.Should().Be(0.066m);
        flour.CostBufferPercent.Should().Be(10m);
        flour.CostPerBaseUnit.Should().Be(0.06m, "the true cost doesn't move");
        (await SubAssemblyCostAsync(subAssemblyId)).Should().Be(66m, "the recipes using it are costed at the new buffered cost");
    }

    [Fact]
    public async Task Re_applying_the_target_to_several_ingredients_works_the_cascade_out_once_however_many_there_are()
    {
        async Task<List<int>> AddWornAsync(string name, int count)
        {
            List<int> ids = [];
            for (var i = 1; i <= count; i++)
            {
                var id = await AddIngredientAsync($"{name} {i}", costPerGram: 0.06m, bufferedCostPerGram: 0.055m);
                await SetTargetAsync(id, 10m);
                ids.Add(id);
            }

            return ids;
        }

        var two = await AddWornAsync("Flour", 2);
        var six = await AddWornAsync("Sugar", 6);
        var repository = CreateRepository();

        int forTwo;
        using (var counter = SqlCommandCounter.Start())
        {
            (await repository.ReapplyTargetBuffersAsync(two, Cancellation)).Should().Be(2);
            forTwo = counter.Commands;
        }

        int forSix;
        using (var counter = SqlCommandCounter.Start())
        {
            (await repository.ReapplyTargetBuffersAsync(six, Cancellation)).Should().Be(6);
            forSix = counter.Commands;
        }

        forSix.Should().Be(forTwo, "the ingredients' graph is loaded and each one reached recomputed once, not once for each re-applied");
    }

    [Fact]
    public async Task A_material_already_at_its_target_or_with_no_price_is_passed_over()
    {
        var atTargetId = await AddIngredientAsync("Sugar", costPerGram: 0.05m, bufferedCostPerGram: 0.055m);
        await SetTargetAsync(atTargetId, 10m);
        var unpricedId = await AddIngredientAsync("Salt");

        (await CreateRepository().ReapplyTargetBuffersAsync([atTargetId, unpricedId], Cancellation)).Should().Be(0);
    }

    private async Task SetTargetAsync(int materialId, decimal target)
    {
        await using var dbContext = CreateDbContext();
        var material = await dbContext.RawMaterials.SingleAsync(m => m.Id == materialId, Cancellation);
        material.TargetCostBufferPercent = target;
        material.CostBufferPercent = target;
        await dbContext.SaveChangesAsync(Cancellation);
    }

    private async Task<decimal> SubAssemblyCostAsync(int subAssemblyId) => (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost;
}
