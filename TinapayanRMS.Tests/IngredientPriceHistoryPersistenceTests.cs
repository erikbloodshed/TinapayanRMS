using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class IngredientPriceHistoryPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private IngredientCostPropagator CreateCostPropagator() =>
        new(CreateDbContextFactory(), new SubAssemblyCostPropagator(CreateDbContextFactory()));

    private async Task<int> SeedPurchasedIngredientAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = "Flour" };
        dbContext.IngredientCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);

        var ingredient = new Ingredient
        {
            Name = "All-Purpose Flour",
            IngredientCategoryId = category.Id,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 25000m,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task<int> SeedMadeIngredientAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = "Fillings" };
        dbContext.IngredientCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);

        var ingredient = new Ingredient
        {
            Name = "Simple Syrup",
            IngredientCategoryId = category.Id,
            Source = IngredientSource.Made,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task<Ingredient> GetIngredientAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Ingredients.AsNoTracking().SingleAsync(i => i.Id == id, Cancellation);
    }

    [Fact]
    public async Task Adding_the_first_price_entry_sets_CurrentCost_and_CostPerGram()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new IngredientPriceHistory
        {
            IngredientId = ingredientId,
            Date = new DateOnly(2026, 1, 1),
            Price = 900m,
        }, Cancellation);

        var ingredient = await GetIngredientAsync(ingredientId);
        ingredient.CurrentCost.Should().Be(900m);
        ingredient.CostPerGram.Should().Be(0.036m);
    }

    [Fact]
    public async Task Adding_a_newer_dated_entry_replaces_CurrentCost()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 2, 1), Price = 950m }, Cancellation);

        var ingredient = await GetIngredientAsync(ingredientId);
        ingredient.CurrentCost.Should().Be(950m);
    }

    [Fact]
    public async Task Adding_an_older_dated_entry_does_not_change_CurrentCost()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 2, 1), Price = 950m }, Cancellation);
        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);

        var ingredient = await GetIngredientAsync(ingredientId);
        ingredient.CurrentCost.Should().Be(950m);
    }

    [Fact]
    public async Task Deleting_the_latest_entry_reverts_CurrentCost_to_the_next_latest()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 2, 1), Price = 950m }, Cancellation);

        var entries = await repository.GetForIngredientAsync(ingredientId, Cancellation);
        var latestEntry = entries.Should().ContainSingle(e => e.Price == 950m).Subject;

        await repository.DeleteAsync(latestEntry.Id, Cancellation);

        var ingredient = await GetIngredientAsync(ingredientId);
        ingredient.CurrentCost.Should().Be(900m);
    }

    [Fact]
    public async Task Deleting_the_only_entry_clears_CurrentCost_and_CostPerGram()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        var entry = (await repository.GetForIngredientAsync(ingredientId, Cancellation)).Should().ContainSingle().Subject;

        await repository.DeleteAsync(entry.Id, Cancellation);

        var ingredient = await GetIngredientAsync(ingredientId);
        ingredient.CurrentCost.Should().BeNull();
        ingredient.CostPerGram.Should().Be(0m);
    }

    [Fact]
    public async Task Updating_an_entrys_price_recomputes_CurrentCost()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        var entry = (await repository.GetForIngredientAsync(ingredientId, Cancellation)).Should().ContainSingle().Subject;

        entry.Price = 1000m;
        await repository.UpdateAsync(entry, Cancellation);

        var ingredient = await GetIngredientAsync(ingredientId);
        ingredient.CurrentCost.Should().Be(1000m);
    }

    [Fact]
    public async Task AddAsync_rejects_a_non_positive_price()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        var act = () => repository.AddAsync(new IngredientPriceHistory
        {
            IngredientId = ingredientId,
            Date = new DateOnly(2026, 1, 1),
            Price = 0m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_price_history_for_a_made_ingredient()
    {
        var ingredientId = await SeedMadeIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        var act = () => repository.AddAsync(new IngredientPriceHistory
        {
            IngredientId = ingredientId,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_nonexistent_ingredient()
    {
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        var act = () => repository.AddAsync(new IngredientPriceHistory
        {
            IngredientId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_entry_for_a_nonexistent_ingredient()
    {
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        var act = () => repository.UpdateAsync(new IngredientPriceHistory
        {
            Id = 999_999,
            IngredientId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_nonexistent_entry()
    {
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());

        var act = () => repository.DeleteAsync(999_999, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_an_ingredient_cascades_to_its_price_history()
    {
        var ingredientId = await SeedPurchasedIngredientAsync();
        var repository = new IngredientPriceHistoryRepository(CreateDbContextFactory(), CreateCostPropagator());
        await repository.AddAsync(new IngredientPriceHistory { IngredientId = ingredientId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Ingredients.Where(i => i.Id == ingredientId).ExecuteDeleteAsync(Cancellation);
        }

        (await repository.GetForIngredientAsync(ingredientId, Cancellation)).Should().BeEmpty();
    }
}
