using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class IngredientPriceHistoryPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialPriceHistoryRepository CreateRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    /// <summary>A purchased flour with a 25,000 g bag and a 1,000 g pack; returns the ingredient's and both units' ids.</summary>
    private async Task<(int IngredientId, int BagId, int PackId)> SeedPurchasedIngredientAsync()
    {
        var bagId = await GetOrCreateUnitOfMeasureAsync("bag", "bag");
        var packId = await GetOrCreateUnitOfMeasureAsync("pack", "pack");

        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Flour" };
        dbContext.RawMaterialCategories.Add(category);
        var bag = new RawMaterialUnit { UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m };
        var pack = new RawMaterialUnit { UnitOfMeasureId = packId, BaseUnitsPerUnit = 1000m };
        var ingredient = new RawMaterial
        {
            Name = "All-Purpose Flour",
            Category = category,
            Source = RawMaterialSource.Purchased,
            Units = [bag, pack],
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return (ingredient.Id, bag.Id, pack.Id);
    }

    private async Task<int> SeedMadeIngredientAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Fillings" };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);

        var ingredient = new RawMaterial
        {
            Name = "Simple Syrup",
            CategoryId = category.Id,
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task<decimal> GetCostPerGramAsync(int ingredientId)
    {
        await using var dbContext = CreateDbContext();
        return (await dbContext.RawMaterials.AsNoTracking().SingleAsync(i => i.Id == ingredientId, Cancellation)).CostPerBaseUnit;
    }

    [Fact]
    public async Task Adding_the_first_price_entry_prices_the_ingredient_per_gram_through_its_unit()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();

        await CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = ingredientId,
            UnitId = bagId,
            Date = new DateOnly(2026, 1, 1),
            Price = 900m,
        }, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.036m); // 900 / 25,000 g
    }

    [Fact]
    public async Task A_price_with_no_unit_is_a_price_per_gram()
    {
        var (ingredientId, _, _) = await SeedPurchasedIngredientAsync();

        await CreateRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, Date = new DateOnly(2026, 1, 1), Price = 0.04m }, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.04m);
    }

    [Fact]
    public async Task A_newer_price_in_another_unit_takes_over_while_the_older_entry_keeps_its_own_unit()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = packId, Date = new DateOnly(2026, 2, 1), Price = 40m }, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.04m); // 40 / 1,000 g

        var entries = await repository.GetForRawMaterialAsync(ingredientId, Cancellation);
        entries.Select(e => (e.Price, e.Unit!.UnitOfMeasure!.Name)).Should().Equal((40m, "pack"), (900m, "bag"));
    }

    [Fact]
    public async Task Adding_a_newer_dated_entry_replaces_the_cost()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 2, 1), Price = 950m }, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.038m);
    }

    [Fact]
    public async Task Adding_a_second_price_on_the_same_date_makes_it_the_current_price()
    {
        // Regression: the new entry has no id until it's saved, so it used to lose the same-date
        // tie to the existing entry and never became the current price.
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 9, 13), Price = 900m }, Cancellation);
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 9, 13), Price = 1000m }, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.04m);
    }

    [Fact]
    public async Task Adding_an_older_dated_entry_does_not_change_the_cost()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 2, 1), Price = 950m }, Cancellation);
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.038m);
    }

    [Fact]
    public async Task Deleting_the_latest_entry_reverts_to_the_next_latest_in_its_own_unit()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = packId, Date = new DateOnly(2026, 2, 1), Price = 40m }, Cancellation);

        var latestEntry = (await repository.GetForRawMaterialAsync(ingredientId, Cancellation)).Should().ContainSingle(e => e.Price == 40m).Subject;
        await repository.DeleteAsync(latestEntry.Id, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.036m); // back to 900 / 25,000 g
    }

    [Fact]
    public async Task Deleting_the_only_entry_zeroes_CostPerGram()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        var entry = (await repository.GetForRawMaterialAsync(ingredientId, Cancellation)).Should().ContainSingle().Subject;

        await repository.DeleteAsync(entry.Id, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0m);
    }

    [Fact]
    public async Task Updating_an_entrys_price_and_unit_recomputes_the_cost()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);
        var entry = (await repository.GetForRawMaterialAsync(ingredientId, Cancellation)).Should().ContainSingle().Subject;

        // Still carries the Unit loaded for display; only the ids and values are saved.
        entry.Price = 50m;
        entry.UnitId = packId;
        await repository.UpdateAsync(entry, Cancellation);

        (await GetCostPerGramAsync(ingredientId)).Should().Be(0.05m);
        (await repository.GetForRawMaterialAsync(ingredientId, Cancellation)).Should().ContainSingle().Which.Unit!.UnitOfMeasure!.Name.Should().Be("pack");
    }

    [Fact]
    public async Task AddAsync_rejects_a_unit_that_belongs_to_another_ingredient()
    {
        var (flourId, _, _) = await SeedPurchasedIngredientAsync();
        var sugarId = await SeedMadeIngredientAsync();
        var sugarKilogramId = await AddKilogramUnitAsync(sugarId);

        var act = () => CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = flourId,
            UnitId = sugarKilogramId,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_non_positive_price()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();

        var act = () => CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = ingredientId,
            UnitId = bagId,
            Date = new DateOnly(2026, 1, 1),
            Price = 0m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_price_history_for_a_made_ingredient()
    {
        var ingredientId = await SeedMadeIngredientAsync();

        var act = () => CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = ingredientId,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_nonexistent_ingredient()
    {
        var act = () => CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_entry_for_a_nonexistent_ingredient()
    {
        var act = () => CreateRepository().UpdateAsync(new RawMaterialPriceHistory
        {
            Id = 999_999,
            RawMaterialId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_entry()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();

        var act = () => CreateRepository().UpdateAsync(new RawMaterialPriceHistory
        {
            Id = 999_999,
            RawMaterialId = ingredientId,
            UnitId = bagId,
            Date = new DateOnly(2026, 1, 1),
            Price = 100m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_nonexistent_entry()
    {
        var act = () => CreateRepository().DeleteAsync(999_999, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_an_ingredient_cascades_to_its_price_history()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = ingredientId, UnitId = bagId, Date = new DateOnly(2026, 1, 1), Price = 900m }, Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(i => i.Id == ingredientId).ExecuteDeleteAsync(Cancellation);
        }

        (await repository.GetForRawMaterialAsync(ingredientId, Cancellation)).Should().BeEmpty();
    }
}
