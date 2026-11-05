using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A cost that doesn't come out even (₱170 a pack of 240 is ₱0.708333… a piece) is worked out to 28 places in memory and
/// stored to 20. Until 2026-10-03 the two were compared as they were, so a Save that changed nothing read the cost as
/// changed: it logged the same price in price history again, and moved the cost's date.
/// </summary>
[Collection(DatabaseCollection.D)]
public class StoredCostPrecisionPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    [Fact]
    public async Task Saving_a_material_again_unchanged_logs_no_second_price_when_its_cost_doesnt_come_out_even()
    {
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var repository = CreateRepository();
        var wrapper = new RawMaterial
        {
            Name = "Cello Sheet", Type = RawMaterialType.Packaging, CategoryId = await AddPackagingCategoryAsync(), BaseUnitId = pieceId,
        };

        var wrapperId = await repository.SaveAsync(wrapper, [new RawMaterialUnit { UnitOfMeasureId = packId, BaseUnitsPerUnit = 240m }], [(null, packId, 170m)], addIns: null, Cancellation);
        var saved = (await repository.GetByIdAsync(wrapperId, Cancellation))!;
        var costDate = saved.CostUpdatedAt;

        await repository.SaveAsync(saved, [.. saved.Units], [(null, packId, 170m)], addIns: null, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialPriceHistories.CountAsync(h => h.RawMaterialId == wrapperId, Cancellation))
            .Should().Be(1, "the price didn't change, so there's nothing new to log");
        var resaved = await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == wrapperId, Cancellation);
        resaved.CostPerBaseUnit.Should().Be(0.70833333333333333333m, "170 ÷ 240, to the 20 places it's stored to");
        resaved.CostUpdatedAt.Should().Be(costDate, "the cost didn't move");
    }

    [Fact]
    public async Task Saving_a_made_ingredient_again_unchanged_leaves_its_cost_date_alone_when_its_cost_doesnt_come_out_even()
    {
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var waterId = await AddIngredientAsync("Water");
        var repository = CreateRepository();
        var dough = new RawMaterial
        {
            Name = "Dough", Type = RawMaterialType.Ingredient, CategoryId = await MadeCategoryIdAsync(), Source = RawMaterialSource.Made, WeightLossPercent = 10m,
            SubAssemblyId = await AddRecipeAsync("Dough Base", [(flourId, 1000m), (waterId, 500m)]),

        };

        // ₱50 of flour over the 1,350 g left of 1,500 g: ₱0.037037… a gram.
        var doughId = await repository.SaveAsync(dough, [], brandPrices: null, addIns: null, Cancellation);
        var saved = (await repository.GetByIdAsync(doughId, Cancellation))!;
        var costDate = saved.CostUpdatedAt;
        costDate.Should().NotBeNull();

        await repository.SaveAsync(saved, [], brandPrices: null, addIns: [], Cancellation);

        var resaved = (await repository.GetByIdAsync(doughId, Cancellation))!;
        resaved.CostPerBaseUnit.Should().Be(0.03703703703703703704m);
        resaved.CostUpdatedAt.Should().Be(costDate, "nothing it's made from changed, so neither did its cost");
    }

    private async Task<int> IngredientsCategoryIdAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterialCategories.Where(c => c.Name == "Ingredients").Select(c => c.Id).SingleAsync(Cancellation);
    }
}
