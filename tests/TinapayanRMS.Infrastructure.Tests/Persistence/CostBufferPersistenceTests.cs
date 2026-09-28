using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A material's cost buffer: its buffered cost (BufferedCostPerBaseUnit) sits beside its true cost,
/// rolls up the BOM tree (counted once, never stacked), and is what sub-assemblies and packaging lines
/// are costed with.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class CostBufferPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
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

    private static RawMaterial MadeIngredient(string name, int categoryId, decimal costBufferPercent = 0m, int id = 0, decimal weightLossPercent = 0m) =>
        new() { Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Made, WeightLossPercent = weightLossPercent, CostBufferPercent = costBufferPercent };

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
            [new RawMaterialUnit { UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }], [(null, bagId, 1250m)], bomLines: null, Cancellation);

        var flour = await GetMaterialAsync(flourId);
        flour.CostPerBaseUnit.Should().Be(0.05m);
        flour.BufferedCostPerBaseUnit.Should().Be(0.0525m);
    }

    [Fact]
    public async Task Changing_a_purchased_ingredients_buffer_reprices_the_sub_assemblies_using_it()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], [(null, null, 0.09m)], bomLines: null, Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", sugarId, 1000m);

        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(90m);

        await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m, id: sugarId), [], brandPrices: null, bomLines: null, Cancellation);

        var sugar = await GetMaterialAsync(sugarId);
        sugar.CostPerBaseUnit.Should().Be(0.09m, "the buffer never changes the true cost");
        sugar.BufferedCostPerBaseUnit.Should().Be(0.099m);
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(99m, "1,000 g at the buffered ₱0.099");
    }

    [Fact]
    public async Task A_made_ingredient_ignores_a_buffer_of_its_own_and_only_rolls_up_its_components()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m), [], [(null, null, 0.10m)], bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId), [], brandPrices: null, [(sugarId, 1000m)], Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", syrupId, 1000m);

        var syrup = await GetMaterialAsync(syrupId);
        syrup.CostPerBaseUnit.Should().Be(0.10m, "the true rollup uses sugar's true cost");
        syrup.BufferedCostPerBaseUnit.Should().Be(0.11m, "the buffered rollup uses sugar's buffered cost");
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(110m);

        // A buffer sent for the made ingredient itself is never applied: sugar's is already in the rollup.
        await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId, costBufferPercent: 10m, id: syrupId), [], brandPrices: null, bomLines: null, Cancellation);

        syrup = await GetMaterialAsync(syrupId);
        syrup.CostPerBaseUnit.Should().Be(0.10m);
        syrup.BufferedCostPerBaseUnit.Should().Be(0.11m, "sugar's buffered ₱0.11, with nothing of syrup's own on top");
        syrup.CostBufferPercent.Should().Be(0m, "a made ingredient is stored with no buffer of its own");
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(110m);
    }

    [Fact]
    public async Task A_made_ingredients_weight_loss_still_divides_its_rollup_while_its_own_buffer_never_multiplies_it()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m), [], [(null, null, 0.10m)], bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(
            MadeIngredient("Simple Syrup", categoryId, costBufferPercent: 10m, weightLossPercent: 10m),
            [],
            brandPrices: null,
            [(sugarId, 1000m)],
            Cancellation);

        var syrup = await GetMaterialAsync(syrupId);
        syrup.CostPerBaseUnit.Should().BeApproximately(0.111111m, 0.000001m, "₱100 over the 900 g it yields");
        syrup.BufferedCostPerBaseUnit.Should().BeApproximately(0.122222m, 0.000001m, "sugar's buffered ₱110 over the same 900 g");
        syrup.CostBufferPercent.Should().Be(0m);
    }

    [Fact]
    public async Task A_made_ingredient_recomputed_by_the_cascade_loses_a_stale_buffer_of_its_own()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m), [], [(null, null, 0.10m)], bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId), [], brandPrices: null, [(sugarId, 1000m)], Cancellation);

        // A percent left on the row from before the rule changed, which no screen can reach.
        await using (var dbContext = CreateDbContext())
        {
            var stale = await dbContext.RawMaterials.SingleAsync(m => m.Id == syrupId, Cancellation);
            stale.CostBufferPercent = 25m;
            stale.BufferedCostPerBaseUnit = 0.1375m;
            await dbContext.SaveChangesAsync(Cancellation);
        }

        // Its component is repriced: the cascade, not a save of the made row, is what recomputes it.
        await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m, id: sugarId), [], [(null, null, 0.20m)], bomLines: null, Cancellation);

        var syrup = await GetMaterialAsync(syrupId);
        syrup.BufferedCostPerBaseUnit.Should().Be(0.22m, "sugar's buffered ₱0.22, with no 25% on top");
        syrup.CostBufferPercent.Should().Be(0m, "the cascade normalises it away");
    }

    [Fact]
    public async Task A_buffer_change_on_a_component_ripples_up_through_the_made_ingredient_into_the_sub_assembly()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], [(null, null, 0.10m)], bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId), [], brandPrices: null, [(sugarId, 1000m)], Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", syrupId, 500m);

        await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 20m, id: sugarId), [], brandPrices: null, bomLines: null, Cancellation);

        (await GetMaterialAsync(syrupId)).BufferedCostPerBaseUnit.Should().Be(0.12m);
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(60m, "500 g of syrup at the buffered ₱0.12");
    }

    [Fact]
    public async Task A_buffer_and_a_new_price_in_the_same_save_use_the_new_buffer()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], [(null, null, 0.09m)], bomLines: null, Cancellation);

        await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 50m, id: sugarId), [], [(null, null, 0.10m)], bomLines: null, Cancellation);

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
        var box = new RawMaterial { Name = "Cake Box", Type = RawMaterialType.Packaging, CategoryId = await AddPackagingCategoryAsync(), BaseUnitId = pieceId };
        var boxId = await repository.SaveAsync(box, [], [(null, null, 30m)], bomLines: null, Cancellation);

        box.Id = boxId;
        box.CostBufferPercent = 10m;
        await repository.SaveAsync(box, [], brandPrices: null, bomLines: null, Cancellation);

        var saved = await GetMaterialAsync(boxId);
        saved.CostPerBaseUnit.Should().Be(30m);
        saved.BufferedCostPerBaseUnit.Should().Be(33m);
        ProductRules.CalculatePackagingLineCost(saved.BufferedCostPerBaseUnit, qtyPerUnit: 0.5m, baseUnitsPerEntryUnit: null)
            .Should().Be(16.5m, "half a box per piece, at the buffered ₱33");
    }

    [Fact]
    public async Task A_new_price_leaves_a_priced_materials_buffered_cost_and_its_sub_assemblies_where_they_were()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m), [], [(null, null, 0.09m)], bomLines: null, Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", sugarId, 1000m);

        (await GetMaterialAsync(sugarId)).BufferedCostPerBaseUnit.Should().Be(0.099m, "worked out from the first price");

        await SetPriceAsync(sugarId, null, 0.12m);

        var sugar = await GetMaterialAsync(sugarId);
        sugar.CostPerBaseUnit.Should().Be(0.12m);
        sugar.BufferedCostPerBaseUnit.Should().Be(0.099m, "a fixed amount, which prices don't move, even above it");
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(99m);

        await RecalculateBufferAsync(sugarId);

        (await GetMaterialAsync(sugarId)).BufferedCostPerBaseUnit.Should().Be(0.132m, "₱0.12 with the 10% on top");
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(132m);
    }

    [Fact]
    public async Task A_typed_buffered_cost_is_saved_as_it_is_and_cascades()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m), [], [(null, null, 0.09m)], bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId), [], brandPrices: null, [(sugarId, 1000m)], Cancellation);
        var custardId = await SeedSubAssemblyAsync("Custard", syrupId, 500m);

        var sugar = PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m, id: sugarId);
        sugar.BufferedCostPerBaseUnit = 0.0518948m; // ₱1,297.37 typed for a 25 kg bag
        await repository.SaveAsync(sugar, [], [(null, null, 0.09m)], bomLines: null, Cancellation);

        (await GetMaterialAsync(sugarId)).BufferedCostPerBaseUnit.Should().Be(0.0518948m, "typed, not ₱0.09 with 10% on top, and not rounded to 4 decimals");
        (await GetMaterialAsync(syrupId)).BufferedCostPerBaseUnit.Should().Be(0.0518948m);
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(25.9474m);
    }

    [Fact]
    public async Task Removing_every_price_leaves_the_material_unpriced_and_its_next_price_works_the_buffer_out_again()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent: 10m), [], [(null, null, 0.09m)], bomLines: null, Cancellation);

        await CreateBrandPriceRepository().SetAsync(sugarId, [], Cancellation);
        (await GetMaterialAsync(sugarId)).BufferedCostPerBaseUnit.Should().Be(0m);

        await SetPriceAsync(sugarId, null, 0.2m);
        (await GetMaterialAsync(sugarId)).BufferedCostPerBaseUnit.Should().Be(0.22m);
    }

    [Fact]
    public async Task A_negative_buffered_cost_is_rejected()
    {
        var categoryId = await SeedCategoryAsync();
        var sugar = PurchasedIngredient("Sugar", categoryId);
        sugar.BufferedCostPerBaseUnit = -0.01m;

        var act = () => CreateRepository().SaveAsync(sugar, [], brandPrices: null, bomLines: null, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Buffered cost can't be negative.");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public async Task A_buffer_outside_0_to_100_percent_is_rejected(decimal costBufferPercent)
    {
        var categoryId = await SeedCategoryAsync();

        var act = () => CreateRepository().SaveAsync(PurchasedIngredient("Sugar", categoryId, costBufferPercent), [], brandPrices: null, bomLines: null, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Cost buffer*");
    }
}
