using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class IngredientBomLinePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private IngredientCostPropagator CreateCostPropagator() =>
        new(CreateDbContextFactory());

    private IngredientBomLineRepository CreateBomRepository() =>
        new(CreateDbContextFactory(), CreateCostPropagator());

    private IngredientPriceHistoryRepository CreatePriceHistoryRepository() =>
        new(CreateDbContextFactory(), CreateCostPropagator());

    private async Task<int> SeedCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = name };
        dbContext.IngredientCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private async Task<int> SeedPurchasedIngredientAsync(string name, int categoryId)
    {
        await using var dbContext = CreateDbContext();
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Purchased,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 1000m,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task<int> SeedMadeIngredientAsync(string name, int categoryId, decimal weightLossPercent = 0m)
    {
        await using var dbContext = CreateDbContext();
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategoryId = categoryId,
            Source = IngredientSource.Made,
            WeightLossPercent = weightLossPercent,
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
    public async Task Setting_lines_computes_CostPerGram_from_the_worked_example()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var waterId = await SeedPurchasedIngredientAsync("Water", categoryId); // never priced: CostPerGram stays 0
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId, weightLossPercent: 20m);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory
        {
            IngredientId = sugarId,
            Date = new DateOnly(2026, 1, 1),
            Price = 90m, // per 1000g purchase unit -> ₱0.09/g
        }, Cancellation);

        await CreateBomRepository().SetLinesAsync(syrupId,
        [
            (sugarId, 1000m),
            (waterId, 500m),
        ], Cancellation);

        var syrup = await GetIngredientAsync(syrupId);
        syrup.CostPerGram.Should().Be(0.075m);
    }

    [Fact]
    public async Task GetForIngredientAsync_returns_lines_with_the_component_populated()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId);

        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 1000m)], Cancellation);

        var lines = await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation);
        var line = lines.Should().ContainSingle().Subject;
        line.Grams.Should().Be(1000m);
        line.ComponentIngredient.Should().NotBeNull();
        line.ComponentIngredient!.Name.Should().Be("Sugar");
    }

    [Fact]
    public async Task A_price_change_ripples_through_a_three_level_chain()
    {
        // sugar -> syrup -> caramel. Changing sugar's price should recompute both
        // syrup and caramel, in that order, without needing to touch caramel directly.
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId);
        var caramelId = await SeedMadeIngredientAsync("Caramel", categoryId);

        var priceHistory = CreatePriceHistoryRepository();
        var bom = CreateBomRepository();

        await priceHistory.AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await bom.SetLinesAsync(syrupId, [(sugarId, 1000m)], Cancellation); // syrup: 1000g @ 0.09/g, no weight loss -> 0.09/g
        await bom.SetLinesAsync(caramelId, [(syrupId, 1000m)], Cancellation); // caramel: 1000g syrup @ 0.09/g -> 0.09/g

        (await GetIngredientAsync(syrupId)).CostPerGram.Should().Be(0.09m);
        (await GetIngredientAsync(caramelId)).CostPerGram.Should().Be(0.09m);

        // Sugar's price doubles.
        await priceHistory.AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 2, 1), Price = 180m }, Cancellation);

        (await GetIngredientAsync(syrupId)).CostPerGram.Should().Be(0.18m);
        (await GetIngredientAsync(caramelId)).CostPerGram.Should().Be(0.18m);
    }

    [Fact]
    public async Task Editing_weight_loss_percent_recomputes_CostPerGram_from_the_current_BOM()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId, weightLossPercent: 0m);

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 1000m)], Cancellation);

        (await GetIngredientAsync(syrupId)).CostPerGram.Should().Be(0.09m);

        var syrup = await GetIngredientAsync(syrupId);
        syrup.WeightLossPercent = 10m;
        await new IngredientRepository(CreateDbContextFactory(), CreateCostPropagator()).UpdateAsync(syrup, Cancellation);

        // 1000g -> 900g output, same ₱90 total cost -> ₱0.10/g.
        (await GetIngredientAsync(syrupId)).CostPerGram.Should().Be(0.1m);
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_cycle()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var aId = await SeedMadeIngredientAsync("A", categoryId);
        var bId = await SeedMadeIngredientAsync("B", categoryId);
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);

        var bom = CreateBomRepository();
        await bom.SetLinesAsync(bId, [(sugarId, 100m)], Cancellation);
        await bom.SetLinesAsync(aId, [(bId, 100m)], Cancellation); // a depends on b

        var act = () => bom.SetLinesAsync(bId, [(aId, 100m)], Cancellation); // b -> a would close the loop

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_an_ingredient_as_its_own_component()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId);

        var act = () => CreateBomRepository().SetLinesAsync(syrupId, [(syrupId, 100m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_nonexistent_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);

        var act = () => CreateBomRepository().SetLinesAsync(999_999, [(sugarId, 100m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task SetLinesAsync_rejects_a_purchased_ingredient()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var flourId = await SeedPurchasedIngredientAsync("Flour", categoryId);

        var act = () => CreateBomRepository().SetLinesAsync(sugarId, [(flourId, 100m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_component_ingredient_that_is_in_use_is_blocked()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId);
        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 100m)], Cancellation);

        await using var dbContext = CreateDbContext();
        var act = () => dbContext.Ingredients.Where(i => i.Id == sugarId).ExecuteDeleteAsync(Cancellation);

        // ExecuteDeleteAsync issues a raw DELETE outside EF Core's change tracker, so an
        // FK violation surfaces as the driver's own SqlException, not a DbUpdateException
        // (that wrapping only happens through SaveChangesAsync).
        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_the_made_ingredient_cascades_to_its_own_BOM_lines()
    {
        var categoryId = await SeedCategoryAsync("Fillings");
        var sugarId = await SeedPurchasedIngredientAsync("Sugar", categoryId);
        var syrupId = await SeedMadeIngredientAsync("Simple Syrup", categoryId);
        await CreateBomRepository().SetLinesAsync(syrupId, [(sugarId, 100m)], Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Ingredients.Where(i => i.Id == syrupId).ExecuteDeleteAsync(Cancellation);
        }

        (await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation)).Should().BeEmpty();
    }
}
