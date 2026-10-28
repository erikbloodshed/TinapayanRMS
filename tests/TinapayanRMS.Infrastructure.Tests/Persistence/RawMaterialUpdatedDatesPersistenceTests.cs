using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A material's two dates, which the Raw Materials header shows in one tile: UpdatedAt, when the form last saved
/// it, and CostUpdatedAt, when its stored cost last moved. They answer different questions — a made ingredient is
/// rarely edited but its cost moves whenever a component's price does — so a save doesn't touch the cost date and
/// a cascade doesn't touch the save date. The cost date is stamped by the DbContext from what actually changed,
/// so every path that writes a cost counts, not just the ones a repository remembers to stamp.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class RawMaterialUpdatedDatesPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>The clock every save here stamps from, moved on between saves, so each date is one the test can name.</summary>
    private readonly TestClock _clock = new(new DateTime(2026, 10, 1, 9, 0, 0));

    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory(_clock));

    private RawMaterialBrandPriceRepository CreateClockedBrandPriceRepository() => new(CreateDbContextFactory(_clock));

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Dry goods" };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private static RawMaterial PurchasedIngredient(string name, int categoryId, int id = 0) =>
        new() { Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Purchased };

    private static RawMaterial MadeIngredient(string name, int categoryId, int subAssemblyId, int id = 0) =>
        new() { Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Made, WeightLossPercent = 0m, SubAssemblyId = subAssemblyId };

    private async Task<RawMaterial> GetMaterialAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == id, Cancellation);
    }

    [Fact]
    public async Task A_new_material_is_stamped_as_saved_but_has_no_cost_date_until_it_costs_something()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();

        var flourId = await repository.SaveAsync(PurchasedIngredient("Flour", categoryId), [], brandPrices: null, addIns: null, Cancellation);

        var flour = await GetMaterialAsync(flourId);
        flour.UpdatedAt.Should().Be(_clock.Now);
        flour.CostUpdatedAt.Should().BeNull("it has no price yet, so its cost has never moved");
    }

    [Fact]
    public async Task A_first_price_stamps_the_cost_date_and_a_save_that_leaves_the_cost_alone_moves_only_the_save_date()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var flourId = await repository.SaveAsync(PurchasedIngredient("Flour", categoryId), [], [(null, null, 0.05m)], addIns: null, Cancellation);

        var priced = await GetMaterialAsync(flourId);
        priced.CostPerBaseUnit.Should().Be(0.05m);
        priced.CostUpdatedAt.Should().Be(_clock.Now, "the first price moved its cost");

        // A rename an hour later: the form saves, the cost doesn't move.
        _clock.Advance(TimeSpan.FromHours(1));
        await repository.SaveAsync(PurchasedIngredient("Bread Flour", categoryId, id: flourId), [], brandPrices: null, addIns: null, Cancellation);

        var renamed = await GetMaterialAsync(flourId);
        renamed.UpdatedAt.Should().Be(_clock.Now, "the form saved it again");
        renamed.CostUpdatedAt.Should().Be(priced.CostUpdatedAt, "nothing about its cost changed");
    }

    [Fact]
    public async Task A_new_price_stamps_the_cost_date_without_touching_the_save_date()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var flourId = await repository.SaveAsync(PurchasedIngredient("Flour", categoryId), [], [(null, null, 0.05m)], addIns: null, Cancellation);
        var priced = await GetMaterialAsync(flourId);

        // The Purchase Cost Setup's own path, an hour later, not a form save of the material.
        _clock.Advance(TimeSpan.FromHours(1));
        await CreateClockedBrandPriceRepository().SetAsync(flourId, [new RawMaterialBrandPrice { Price = 0.08m }], Cancellation);

        var repriced = await GetMaterialAsync(flourId);
        repriced.CostPerBaseUnit.Should().Be(0.08m);
        repriced.CostUpdatedAt.Should().Be(_clock.Now);
        repriced.UpdatedAt.Should().Be(priced.UpdatedAt, "a price isn't a save of the material");
    }

    [Fact]
    public async Task A_components_price_change_stamps_the_made_ingredients_cost_date_through_the_cascade()
    {
        var categoryId = await SeedCategoryAsync();
        var madeCategoryId = await MadeCategoryIdAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], [(null, null, 0.10m)], addIns: null, Cancellation);
        var syrupId = await repository.SaveAsync(
            MadeIngredient("Simple Syrup", madeCategoryId, await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)])), [], brandPrices: null, addIns: null, Cancellation);

        var syrupBefore = await GetMaterialAsync(syrupId);
        syrupBefore.CostPerBaseUnit.Should().Be(0.10m);
        syrupBefore.CostUpdatedAt.Should().NotBeNull("its recipe gave it a cost");

        _clock.Advance(TimeSpan.FromHours(1));
        await CreateClockedBrandPriceRepository().SetAsync(sugarId, [new RawMaterialBrandPrice { Price = 0.20m }], Cancellation);

        var syrupAfter = await GetMaterialAsync(syrupId);
        syrupAfter.CostPerBaseUnit.Should().Be(0.20m, "the cascade recomputed it");
        syrupAfter.CostUpdatedAt.Should().Be(_clock.Now, "the cascade moved its cost, so the cost date follows");
        syrupAfter.UpdatedAt.Should().Be(syrupBefore.UpdatedAt, "nobody saved the syrup");
    }

    [Fact]
    public async Task A_cascade_that_leaves_a_cost_where_it_was_leaves_its_cost_date_alone()
    {
        var categoryId = await SeedCategoryAsync();
        var madeCategoryId = await MadeCategoryIdAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], [(null, null, 0.10m)], addIns: null, Cancellation);
        var syrupId = await repository.SaveAsync(
            MadeIngredient("Simple Syrup", madeCategoryId, await AddRecipeAsync("Syrup Base", [(sugarId, 1000m)])), [], brandPrices: null, addIns: null, Cancellation);
        var before = await GetMaterialAsync(syrupId);

        // The same price again: the cascade runs and writes the same figures.
        _clock.Advance(TimeSpan.FromHours(1));
        await CreateClockedBrandPriceRepository().SetAsync(sugarId, [new RawMaterialBrandPrice { Price = 0.10m }], Cancellation);

        var after = await GetMaterialAsync(syrupId);
        after.CostPerBaseUnit.Should().Be(0.10m);
        after.CostUpdatedAt.Should().Be(before.CostUpdatedAt, "the figures were rewritten, but none of them changed");
    }
}
