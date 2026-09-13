using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class IngredientPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private IngredientCostPropagator CreateCostPropagator() =>
        new(CreateDbContextFactory(), new SubAssemblyCostPropagator(CreateDbContextFactory()));

    private IngredientRepository CreateRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

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
        var repository = CreateRepository();

        await repository.AddAsync(new Ingredient
        {
            Name = "All-Purpose Flour",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
        }, Cancellation);

        var ingredients = await repository.GetAllAsync(Cancellation);
        var ingredient = ingredients.Should().ContainSingle().Subject;
        ingredient.Name.Should().Be("All-Purpose Flour");
        ingredient.Source.Should().Be(IngredientSource.Purchased);
        ingredient.IngredientCategory.Should().NotBeNull();
        ingredient.Units.Should().BeEmpty();
    }

    [Fact]
    public async Task A_made_ingredient_can_be_added_with_a_weight_loss_percent()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new Ingredient
        {
            Name = "Simple Syrup",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = 20m,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Source.Should().Be(IngredientSource.Made);
        ingredient.WeightLossPercent.Should().Be(20m);
    }

    [Fact]
    public async Task AddAsync_rejects_a_made_ingredient_without_a_weight_loss_percent()
    {
        var categoryId = await SeedCategoryAsync();

        var act = () => CreateRepository().AddAsync(new Ingredient
        {
            Name = "Invalid",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_changing_an_ingredients_source()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new Ingredient
        {
            Name = "Sugar",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
        }, Cancellation);

        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Source = IngredientSource.Made;
        ingredient.WeightLossPercent = 20m;

        var act = () => repository.UpdateAsync(ingredient, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task GetAllAsync_and_GetByIdAsync_include_the_units_largest_first_with_their_unit_of_measure()
    {
        var categoryId = await SeedCategoryAsync();
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.Ingredients.Add(new Ingredient
            {
                Name = "Flour",
                IngredientCategoryId = categoryId,
                Source = IngredientSource.Purchased,
                Units =
                [
                    new IngredientUnit { UnitOfMeasureId = packId, GramsPerUnit = 1000m, Description = "Small pack (1 kg)" },
                    new IngredientUnit { UnitOfMeasureId = bagId, GramsPerUnit = 25000m },
                ],
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var repository = CreateRepository();
        var fromAll = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var byId = await repository.GetByIdAsync(fromAll.Id, Cancellation);

        foreach (var ingredient in new[] { fromAll, byId! })
        {
            ingredient.IngredientCategory.Should().NotBeNull();
            ingredient.Units.Select(u => u.UnitOfMeasure!.Name).Should().Equal("bag", "pack");
            ingredient.Units[1].Description.Should().Be("Small pack (1 kg)");
        }
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_a_nonexistent_ingredient()
    {
        (await CreateRepository().GetByIdAsync(999_999, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_renames_a_purchased_ingredient_and_preserves_its_CostPerGram()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var priceHistoryRepository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new Ingredient
        {
            Name = "Sugar",
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
        }, Cancellation);
        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var kilogramUnitId = await AddKilogramUnitAsync(ingredient.Id);
        await priceHistoryRepository.AddAsync(new IngredientPriceHistory { IngredientId = ingredient.Id, IngredientUnitId = kilogramUnitId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);

        ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        ingredient.Name = "Refined Sugar";
        ingredient.CostPerGram = 999m; // the form never owns this
        await repository.UpdateAsync(ingredient, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Refined Sugar");
        // A blind dbContext.Set<T>().Update(incoming) regression would overwrite this server-owned
        // field on every plain rename (PLAN.md's documented Phase 6 bug).
        updated.CostPerGram.Should().Be(0.09m);
        updated.Units.Should().ContainSingle("a rename leaves the units alone");
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_ingredient()
    {
        var categoryId = await SeedCategoryAsync();

        var act = () => CreateRepository().UpdateAsync(new Ingredient
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
        var repository = CreateRepository();

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
    public async Task DeleteAsync_removes_an_ingredient_along_with_its_units_and_the_prices_recorded_in_them()
    {
        // Price history references the ingredient (Cascade) and one of its units (Restrict), and
        // the units cascade from the ingredient too: SQL Server must accept removing all three.
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        await repository.AddAsync(new Ingredient { Name = "Flour", IngredientCategoryId = categoryId, Source = IngredientSource.Purchased }, Cancellation);
        var ingredient = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var kilogramUnitId = await AddKilogramUnitAsync(ingredient.Id);
        await new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator()).AddAsync(
            new IngredientPriceHistory { IngredientId = ingredient.Id, IngredientUnitId = kilogramUnitId, Date = new DateOnly(2026, 1, 1), Price = 50m },
            Cancellation);

        await repository.DeleteAsync(ingredient.Id, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.Ingredients.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.IngredientUnits.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.IngredientPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_ingredient_names_are_rejected()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();

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
