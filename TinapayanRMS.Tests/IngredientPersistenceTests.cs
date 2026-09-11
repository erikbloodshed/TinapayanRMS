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
        new(CreateDbContextFactory(), new SubAssemblyCostPropagator(CreateDbContextFactory()));

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
    public async Task GetByIdAsync_returns_the_ingredient_with_related_data_populated()
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
        var seeded = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;

        var byId = await repository.GetByIdAsync(seeded.Id, Cancellation);

        byId.Should().NotBeNull();
        byId!.Name.Should().Be("Sugar");
        byId.IngredientCategory.Should().NotBeNull();
        byId.PurchaseUnit.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_a_nonexistent_ingredient()
    {
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());

        (await repository.GetByIdAsync(999_999, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_renames_a_purchased_ingredient_and_preserves_CurrentCost_while_recomputing_CostPerGram()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator());
        var priceHistoryRepository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new Ingredient
        {
            Name = "Sugar",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 1000m,
        }, Cancellation);
        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        await priceHistoryRepository.AddAsync(new IngredientPriceHistory { IngredientId = ingredient.Id, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);

        // Rename and correct a mistyped purchase-unit quantity -- no new price entry.
        ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Name = "Refined Sugar";
        ingredient.PurchaseUnitQty = 900m;
        await repository.UpdateAsync(ingredient, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Refined Sugar");
        updated.PurchaseUnitQty.Should().Be(900m);
        // A blind dbContext.Set<T>().Update(incoming) regression would silently zero
        // these two server-owned fields on every plain rename (PLAN.md's documented
        // Phase 6 bug) -- they must survive, and CostPerGram must reflect the new qty.
        updated.CurrentCost.Should().Be(90m);
        updated.CostPerGram.Should().Be(0.1m); // 90 / 900
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
