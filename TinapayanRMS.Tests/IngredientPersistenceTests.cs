using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class IngredientPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private IngredientCostPropagator CreateCostPropagator() =>
        new(CreateDbContextFactory());

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = "Flour" };
        dbContext.IngredientCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    [Fact]
    public async Task A_purchased_ingredient_can_be_added_and_read_back()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new Ingredient
        {
            Name = "All-Purpose Flour",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 25000m,
        }, Cancellation);

        var ingredients = await repository.GetAllAsync(Cancellation);
        var ingredient = ingredients.Should().ContainSingle().Subject;
        ingredient.Name.Should().Be("All-Purpose Flour");
        ingredient.Source.Should().Be(IngredientSource.Purchased);
        ingredient.PurchaseUnitQty.Should().Be(25000m);
        ingredient.IngredientCategory.Should().NotBeNull();
        ingredient.PurchaseUnit.Should().NotBeNull();
    }

    [Fact]
    public async Task A_made_ingredient_can_be_added_with_no_purchase_fields()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new Ingredient
        {
            Name = "Simple Syrup",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = 20m,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Source.Should().Be(IngredientSource.Made);
        ingredient.PurchaseUnitId.Should().BeNull();
        ingredient.PurchaseUnitQty.Should().BeNull();
    }

    [Fact]
    public async Task AddAsync_rejects_a_purchased_ingredient_without_a_purchase_unit()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        var act = () => repository.AddAsync(new Ingredient
        {
            Name = "Invalid",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_changing_an_ingredients_source()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new Ingredient
        {
            Name = "Sugar",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 1000m,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Source = IngredientSource.Made;
        ingredient.PurchaseUnitId = null;
        ingredient.PurchaseUnitQty = null;
        ingredient.WeightLossPercent = 20m;

        var act = () => repository.UpdateAsync(ingredient, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_ingredient()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        var act = () => repository.UpdateAsync(new Ingredient
        {
            Id = 999_999,
            Name = "Ghost",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = 10m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_ingredient()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new Ingredient
        {
            Name = "Water",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = 0m,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        await repository.DeleteAsync(ingredient.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_ingredient_names_are_rejected()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new Ingredient
        {
            Name = "Butter",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = 0m,
        }, Cancellation);

        var act = () => repository.AddAsync(new Ingredient
        {
            Name = "Butter",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = 0m,
        }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
