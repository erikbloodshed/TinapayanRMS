using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// A material's cost buffer: its buffered cost (BufferedCostPerBaseUnit) sits beside its true cost,
/// stacks up the BOM tree, and is what sub-assemblies and packaging lines are costed with.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CostBufferPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static readonly DateOnly Today = new(2026, 9, 14);

    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Dry goods" };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    /// <summary>A sub-assembly of <paramref name="grams"/> of one ingredient, in a product of its own.</summary>
    private async Task<int> SeedSubAssemblyAsync(string name, int ingredientId, decimal grams)
    {
        var roleId = await AddRoleAsync("Filling");
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync($"{name} Bun", roleId, [(ingredientId, grams)]);
        return subAssemblyId;
    }

    private static RawMaterial PurchasedIngredient(string name, int categoryId, decimal costBufferPercent = 0m, int id = 0) =>
        new() { Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Purchased, CostBufferPercent = costBufferPercent };

    private static RawMaterial MadeIngredient(string name, int categoryId, decimal costBufferPercent = 0m, int id = 0) =>
        new() { Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Made, WeightLossPercent = 0m, CostBufferPercent = costBufferPercent };

    private async Task<RawMaterial> GetMaterialAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == id, Cancellation);
    }

    private async Task<decimal> GetSubAssemblyTotalCostAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return (await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == id, Cancellation)).TotalCost;
    }

    [Fact]
    public async Task A_new_price_gets_the_materials_buffer_on_top_and_keeps_the_true_cost()
    {
        var categoryId = await SeedCategoryAsync();
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");

        var flourId = await CreateRepository().SaveAsync(
            PurchasedIngredient("Flour", categoryId, costBufferPercent: 5m),
            [new RawMaterialUnit { UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }], (Today, bagId, 1250m), bomLines: null, Cancellation);

        var flour = await GetMaterialAsync(flourId);
        flour.CostPerBaseUnit.Should().Be(0.05m);
        flour.BufferedCostPerBaseUnit.Should().Be(0.0525m);
    }

    [Fact]
    public async Task Changing_a_purchased_ingredients_buffer_reprices_the_sub_assemblies_using_it()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], (Today, null, 0.09m), bomLines: null, Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", sugarId, 1000m);

        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(90m);

        await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m, id: sugarId), [], newPrice: null, bomLines: null, Cancellation);

        var sugar = await GetMaterialAsync(sugarId);
        sugar.CostPerBaseUnit.Should().Be(0.09m, "the buffer never changes the true cost");
        sugar.BufferedCostPerBaseUnit.Should().Be(0.099m);
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(99m, "1,000 g at the buffered ₱0.099");
    }

    [Fact]
    public async Task A_made_ingredient_stacks_its_own_buffer_on_its_components_buffered_costs()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m), [], (Today, null, 0.10m), bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId), [], newPrice: null, [(sugarId, 1000m)], Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", syrupId, 1000m);

        var syrup = await GetMaterialAsync(syrupId);
        syrup.CostPerBaseUnit.Should().Be(0.10m, "the true rollup uses sugar's true cost");
        syrup.BufferedCostPerBaseUnit.Should().Be(0.11m, "the buffered rollup uses sugar's buffered cost");
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(110m);

        await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId, costBufferPercent: 10m, id: syrupId), [], newPrice: null, bomLines: null, Cancellation);

        syrup = await GetMaterialAsync(syrupId);
        syrup.CostPerBaseUnit.Should().Be(0.10m);
        syrup.BufferedCostPerBaseUnit.Should().Be(0.121m, "syrup's 10% on top of sugar's buffered ₱0.11");
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(121m);
    }

    [Fact]
    public async Task A_buffer_change_on_a_component_ripples_up_through_the_made_ingredient_into_the_sub_assembly()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], (Today, null, 0.10m), bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId), [], newPrice: null, [(sugarId, 1000m)], Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", syrupId, 500m);

        await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 20m, id: sugarId), [], newPrice: null, bomLines: null, Cancellation);

        (await GetMaterialAsync(syrupId)).BufferedCostPerBaseUnit.Should().Be(0.12m);
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(60m, "500 g of syrup at the buffered ₱0.12");
    }

    [Fact]
    public async Task A_buffer_and_a_new_price_in_the_same_save_use_the_new_buffer()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], (Today, null, 0.09m), bomLines: null, Cancellation);

        await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 50m, id: sugarId), [], (Today, null, 0.10m), bomLines: null, Cancellation);

        var sugar = await GetMaterialAsync(sugarId);
        sugar.CostPerBaseUnit.Should().Be(0.10m);
        sugar.BufferedCostPerBaseUnit.Should().Be(0.15m);
    }

    [Fact]
    public async Task A_packaging_materials_buffer_reaches_its_packaging_line_cost()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        var box = new RawMaterial { Name = "Cake Box", Type = RawMaterialType.Packaging, CategoryId = categoryId, BaseUnitId = pieceId };
        var boxId = await repository.SaveAsync(box, [], (Today, null, 30m), bomLines: null, Cancellation);

        box.Id = boxId;
        box.CostBufferPercent = 10m;
        await repository.SaveAsync(box, [], newPrice: null, bomLines: null, Cancellation);

        var saved = await GetMaterialAsync(boxId);
        saved.CostPerBaseUnit.Should().Be(30m);
        saved.BufferedCostPerBaseUnit.Should().Be(33m);
        ProductRules.CalculatePackagingLineCostPerPiece(saved.BufferedCostPerBaseUnit, qtyPerPiece: 0.5m, baseUnitsPerEntryUnit: null)
            .Should().Be(16.5m, "half a box per piece, at the buffered ₱33");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task A_buffer_outside_0_to_100_percent_is_rejected(decimal costBufferPercent)
    {
        var categoryId = await SeedCategoryAsync();

        var act = () => CreateRepository().SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent), [], newPrice: null, bomLines: null, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cost buffer*");
    }
}
