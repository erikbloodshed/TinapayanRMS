using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class IngredientUnitPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private IngredientCostPropagator CreateCostPropagator() =>
        new(CreateDbContextFactory(), new SubAssemblyCostPropagator(CreateDbContextFactory()));

    private IngredientUnitRepository CreateRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    private IngredientPriceHistoryRepository CreatePriceHistoryRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    private async Task<int> SeedIngredientAsync(string name, IngredientSource source = IngredientSource.Purchased)
    {
        await using var dbContext = CreateDbContext();
        var category = await dbContext.IngredientCategories.FirstOrDefaultAsync(Cancellation) ?? new IngredientCategory { Name = "Dry goods" };
        var ingredient = new Ingredient
        {
            Name = name,
            IngredientCategory = category,
            Source = source,
            WeightLossPercent = source == IngredientSource.Made ? 0m : null,
        };
        dbContext.Ingredients.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return ingredient.Id;
    }

    private async Task<decimal> GetCostPerGramAsync(int ingredientId)
    {
        await using var dbContext = CreateDbContext();
        return (await dbContext.Ingredients.AsNoTracking().SingleAsync(i => i.Id == ingredientId, Cancellation)).CostPerGram;
    }

    [Fact]
    public async Task AddAsync_and_GetForIngredientAsync_round_trip_units_largest_first()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();

        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = packId, GramsPerUnit = 1000m, Description = "Small pack (1 kg)" }, Cancellation);
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m, Description = "Standard flour bag (25 kg)" }, Cancellation);

        var units = await repository.GetForIngredientAsync(flourId, Cancellation);
        units.Select(u => (u.UnitOfMeasure!.Name, u.GramsPerUnit, u.Description))
            .Should().Equal(("bag", 25000m, "Standard flour bag (25 kg)"), ("pack", 1000m, "Small pack (1 kg)"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task AddAsync_rejects_a_multiplier_that_is_not_positive(decimal gramsPerUnit)
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");

        var act = () => CreateRepository().AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = gramsPerUnit }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_unit_the_ingredient_already_has()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);

        var act = () => repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 50000m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has*");
    }

    [Fact]
    public async Task AddAsync_rejects_a_third_unit()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var cupId = await SeedUnitOfMeasureAsync("cup", "cup");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = packId, GramsPerUnit = 1000m }, Cancellation);

        var act = () => repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = cupId, GramsPerUnit = 120m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at most 2*");
        (await repository.GetForIngredientAsync(flourId, Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task The_same_unit_of_measure_can_be_an_alternative_unit_of_different_ingredients()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var sugarId = await SeedIngredientAsync("Sugar");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();

        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new IngredientUnit { IngredientId = sugarId, UnitOfMeasureId = bagId, GramsPerUnit = 50000m }, Cancellation);

        (await repository.GetForIngredientAsync(sugarId, Cancellation)).Should().ContainSingle().Which.GramsPerUnit.Should().Be(50000m);
    }

    [Fact]
    public async Task AddAsync_rejects_a_nonexistent_ingredient()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");

        var act = () => CreateRepository().AddAsync(new IngredientUnit { IngredientId = 999_999, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Correcting_the_multiplier_of_the_latest_prices_unit_reprices_the_ingredient_and_everything_built_from_it()
    {
        var sugarId = await SeedIngredientAsync("Sugar");
        var syrupId = await SeedIngredientAsync("Simple Syrup", IngredientSource.Made);
        var sackUnitOfMeasureId = await SeedUnitOfMeasureAsync("sack", "sk");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = sugarId, UnitOfMeasureId = sackUnitOfMeasureId, GramsPerUnit = 1000m }, Cancellation);
        var sack = (await repository.GetForIngredientAsync(sugarId, Cancellation)).Single();

        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = sugarId, IngredientUnitId = sack.Id, Date = new DateOnly(2026, 1, 1), Price = 90m }, Cancellation);
        await new IngredientBomLineRepository(CreateDbContextFactory(), CreateCostPropagator()).SetLinesAsync(syrupId, [(sugarId, null, 1000m)], Cancellation);

        int subAssemblyId;
        await using (var dbContext = CreateDbContext())
        {
            var subAssembly = new SubAssembly { Name = "Custard", Role = new SubAssemblyRole { Name = "Filling", SortOrder = 1 } };
            dbContext.SubAssemblies.Add(subAssembly);
            await dbContext.SaveChangesAsync(Cancellation);
            subAssemblyId = subAssembly.Id;
        }
        await new SubAssemblyLineRepository(CreateDbContextFactory()).SetLinesAsync(subAssemblyId, [(sugarId, null, 500m)], Cancellation);

        (await GetCostPerGramAsync(sugarId)).Should().Be(0.09m);

        // The sack actually holds 900 g, not 1,000 g.
        sack.GramsPerUnit = 900m;
        await repository.UpdateAsync(sack, Cancellation);

        (await GetCostPerGramAsync(sugarId)).Should().Be(0.1m); // 90 / 900 g
        (await GetCostPerGramAsync(syrupId)).Should().Be(0.1m, "the made ingredient using sugar is recomputed");
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == subAssemblyId, Cancellation)).TotalCost
                .Should().Be(50m, "the sub-assembly using sugar is recomputed: 500 g at 0.10/g");
        }
    }

    [Fact]
    public async Task Correcting_a_multiplier_keeps_each_recipe_lines_quantity_and_moves_its_grams_and_totals()
    {
        var eggId = await SeedIngredientAsync("Egg");
        var custardId = await SeedIngredientAsync("Custard Base", IngredientSource.Made);
        var pieceUnitOfMeasureId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = eggId, UnitOfMeasureId = pieceUnitOfMeasureId, GramsPerUnit = 50m }, Cancellation);
        var piece = (await repository.GetForIngredientAsync(eggId, Cancellation)).Single();
        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = eggId, Date = new DateOnly(2026, 1, 1), Price = 0.2m }, Cancellation); // per gram

        await new IngredientBomLineRepository(CreateDbContextFactory(), CreateCostPropagator()).SetLinesAsync(custardId, [(eggId, piece.Id, 2m)], Cancellation);
        int subAssemblyId;
        await using (var dbContext = CreateDbContext())
        {
            var subAssembly = new SubAssembly { Name = "Egg Wash", Role = new SubAssemblyRole { Name = "Topping", SortOrder = 1 } };
            dbContext.SubAssemblies.Add(subAssembly);
            await dbContext.SaveChangesAsync(Cancellation);
            subAssemblyId = subAssembly.Id;
        }
        await new SubAssemblyLineRepository(CreateDbContextFactory()).SetLinesAsync(subAssemblyId, [(eggId, piece.Id, 3m)], Cancellation);

        // The eggs are bigger than recorded: 60 g each.
        piece.GramsPerUnit = 60m;
        await repository.UpdateAsync(piece, Cancellation);

        await using var check = CreateDbContext();
        var subAssemblyLine = await check.SubAssemblyLines.AsNoTracking().SingleAsync(Cancellation);
        subAssemblyLine.Quantity.Should().Be(3m);
        subAssemblyLine.Grams.Should().Be(180m);
        var bomLine = await check.IngredientBomLines.AsNoTracking().SingleAsync(Cancellation);
        bomLine.Quantity.Should().Be(2m);
        bomLine.Grams.Should().Be(120m);

        var subAssemblyAfter = await check.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == subAssemblyId, Cancellation);
        subAssemblyAfter.TotalWeight.Should().Be(180m, "the sub-assembly's weight follows its lines' grams");
        subAssemblyAfter.TotalCost.Should().Be(36m); // 180 g at 0.20/g
        (await GetCostPerGramAsync(eggId)).Should().Be(0.2m, "a price per gram doesn't depend on the piece's weight");
        (await GetCostPerGramAsync(custardId)).Should().Be(0.2m);
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_unit_a_recipe_line_is_entered_in()
    {
        var eggId = await SeedIngredientAsync("Egg");
        var custardId = await SeedIngredientAsync("Custard Base", IngredientSource.Made);
        var pieceUnitOfMeasureId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = eggId, UnitOfMeasureId = pieceUnitOfMeasureId, GramsPerUnit = 50m }, Cancellation);
        var piece = (await repository.GetForIngredientAsync(eggId, Cancellation)).Single();
        await new IngredientBomLineRepository(CreateDbContextFactory(), CreateCostPropagator()).SetLinesAsync(custardId, [(eggId, piece.Id, 2m)], Cancellation);

        var act = () => repository.DeleteAsync(piece.Id, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'piece' is used by 1 recipe line*");
    }

    [Fact]
    public async Task Correcting_a_multiplier_the_latest_price_is_not_in_leaves_the_cost_alone()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = packId, GramsPerUnit = 1000m }, Cancellation);
        var units = await repository.GetForIngredientAsync(flourId, Cancellation);
        var bag = units.Single(u => u.UnitOfMeasureId == bagId);
        var pack = units.Single(u => u.UnitOfMeasureId == packId);
        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = flourId, IngredientUnitId = bag.Id, Date = new DateOnly(2026, 1, 1), Price = 1250m }, Cancellation);

        pack.GramsPerUnit = 500m;
        await repository.UpdateAsync(pack, Cancellation);

        (await GetCostPerGramAsync(flourId)).Should().Be(0.05m);
    }

    [Fact]
    public async Task UpdateAsync_can_rename_the_unit_and_description_but_not_to_a_unit_the_ingredient_already_has()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = packId, GramsPerUnit = 1000m }, Cancellation);
        var bag = (await repository.GetForIngredientAsync(flourId, Cancellation)).Single(u => u.UnitOfMeasureId == bagId);

        bag.UnitOfMeasureId = sackId;
        bag.Description = "25 kg sack";
        await repository.UpdateAsync(bag, Cancellation);

        var renamed = (await repository.GetForIngredientAsync(flourId, Cancellation)).Single(u => u.Id == bag.Id);
        renamed.UnitOfMeasure!.Name.Should().Be("sack");
        renamed.Description.Should().Be("25 kg sack");

        renamed.UnitOfMeasureId = packId;
        var act = () => repository.UpdateAsync(renamed, Cancellation);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_an_unused_unit()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);
        var bag = (await repository.GetForIngredientAsync(flourId, Cancellation)).Single();

        await repository.DeleteAsync(bag.Id, Cancellation);

        (await repository.GetForIngredientAsync(flourId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_unit_a_price_is_recorded_in()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();
        await repository.AddAsync(new IngredientUnit { IngredientId = flourId, UnitOfMeasureId = bagId, GramsPerUnit = 25000m }, Cancellation);
        var bag = (await repository.GetForIngredientAsync(flourId, Cancellation)).Single();
        await CreatePriceHistoryRepository().AddAsync(new IngredientPriceHistory { IngredientId = flourId, IngredientUnitId = bag.Id, Date = new DateOnly(2026, 1, 1), Price = 1250m }, Cancellation);

        var act = () => repository.DeleteAsync(bag.Id, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'bag' is used by 1 price entry*");
        (await repository.GetForIngredientAsync(flourId, Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_nonexistent_unit()
    {
        var act = () => CreateRepository().DeleteAsync(999_999, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
