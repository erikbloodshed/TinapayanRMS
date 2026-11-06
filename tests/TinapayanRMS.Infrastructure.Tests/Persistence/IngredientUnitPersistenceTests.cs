using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class IngredientUnitPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialUnitRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedIngredientAsync(string name, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        await using var dbContext = CreateDbContext();
        var category = await dbContext.RawMaterialCategories.FirstOrDefaultAsync(Cancellation) ?? new RawMaterialCategory { Name = "Dry goods" };
        var ingredient = new RawMaterial
        {
            Name = name,
            Category = category,
            Source = source,
            WeightLossPercent = source == RawMaterialSource.Made ? 0m : null,
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

    [Test]
    public async Task AddAsync_and_GetForIngredientAsync_round_trip_units_largest_first()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = packId, BaseUnitsPerUnit = 1000m, Description = "Small pack (1 kg)" }, Cancellation);
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m, Description = "Standard flour bag (25 kg)" }, Cancellation);

        var units = await repository.GetForRawMaterialAsync(flourId, Cancellation);
        units.Select(u => (u.UnitOfMeasure!.Name, u.BaseUnitsPerUnit, u.Description))
            .Should().Equal(("bag", 25000m, "Standard flour bag (25 kg)"), ("pack", 1000m, "Small pack (1 kg)"));
    }

    [Test]
    public async Task A_kilogram_of_an_ingredient_is_saved_as_1000_grams_whatever_multiplier_was_sent()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var kilogramId = await SeedUnitOfMeasureAsync("kilogram", "kg");
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = kilogramId, BaseUnitsPerUnit = 0m }, Cancellation);
        var saved = (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Single();
        saved.BaseUnitsPerUnit.Should().Be(1000m, "a kilogram is 1,000 g by definition, so nothing typed changes it");

        await repository.UpdateAsync(new RawMaterialUnit { Id = saved.Id, RawMaterialId = flourId, UnitOfMeasureId = kilogramId, BaseUnitsPerUnit = 25000m }, Cancellation);
        (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Single().BaseUnitsPerUnit.Should().Be(1000m);
    }

    [Test]
    [Arguments(0)]
    [Arguments(-5)]
    public async Task AddAsync_rejects_a_multiplier_that_is_not_positive(decimal gramsPerUnit)
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");

        var act = () => CreateRepository().AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = gramsPerUnit }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task AddAsync_rejects_a_unit_the_ingredient_already_has()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);

        var act = () => repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 50000m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has*");
    }

    [Test]
    public async Task AddAsync_accepts_a_third_unit()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var cupId = await SeedUnitOfMeasureAsync("cup", "cup");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = packId, BaseUnitsPerUnit = 1000m }, Cancellation);

        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = cupId, BaseUnitsPerUnit = 120m }, Cancellation);

        (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Select(u => u.UnitOfMeasureId)
            .Should().Equal([bagId, packId, cupId], "there's no limit on alternative units, and they're listed largest first");
    }

    [Test]
    public async Task The_same_unit_of_measure_can_be_an_alternative_unit_of_different_ingredients()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var sugarId = await SeedIngredientAsync("Sugar");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = sugarId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 50000m }, Cancellation);

        (await repository.GetForRawMaterialAsync(sugarId, Cancellation)).Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(50000m);
    }

    [Test]
    public async Task AddAsync_rejects_a_nonexistent_ingredient()
    {
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");

        var act = () => CreateRepository().AddAsync(new RawMaterialUnit { RawMaterialId = 999_999, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task Correcting_the_multiplier_of_the_latest_prices_unit_reprices_the_ingredient_and_everything_built_from_it()
    {
        var sugarId = await SeedIngredientAsync("Sugar");
        var sackUnitOfMeasureId = await SeedUnitOfMeasureAsync("sack", "sk");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = sugarId, UnitOfMeasureId = sackUnitOfMeasureId, BaseUnitsPerUnit = 1000m }, Cancellation);
        var sack = (await repository.GetForRawMaterialAsync(sugarId, Cancellation)).Single();

        await SetPriceAsync(sugarId, sack.Id, 90m);
        var syrupId = await AddMadeIngredientAsync("Simple Syrup", [(sugarId, 1000m)]);
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync("Custard Bun", await AddRoleAsync("Filling"), [(sugarId, 500m)]);

        (await GetCostPerGramAsync(sugarId)).Should().Be(0.09m);

        // The sack actually holds 900 g, not 1,000 g.
        sack.BaseUnitsPerUnit = 900m;
        await repository.UpdateAsync(sack, Cancellation);
        await RecalculateBufferAsync(sugarId); // the buffered cost recipes use is fixed until it's recalculated

        (await GetCostPerGramAsync(sugarId)).Should().Be(0.1m); // 90 / 900 g
        (await GetCostPerGramAsync(syrupId)).Should().Be(0.1m, "the made ingredient using sugar is recomputed");
        (await SubAssemblyTotalsAsync(subAssemblyId)).TotalCost.Should().Be(50m, "the sub-assembly using sugar is costed at its new cost: 500 g at 0.10/g");
    }

    [Test]
    public async Task Correcting_a_multiplier_leaves_recipe_lines_and_their_totals_alone_since_they_are_in_grams()
    {
        var eggId = await SeedIngredientAsync("Egg");
        var pieceUnitOfMeasureId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = eggId, UnitOfMeasureId = pieceUnitOfMeasureId, BaseUnitsPerUnit = 50m }, Cancellation);
        var piece = (await repository.GetForRawMaterialAsync(eggId, Cancellation)).Single();
        await SetPriceAsync(eggId, null, 0.2m); // per gram

        var custardId = await AddMadeIngredientAsync("Custard Base", [(eggId, 100m)]);
        var custardRecipeId = await RecipeIdOfAsync(custardId);
        var (_, subAssemblyId) = await AddProductWithSubAssemblyAsync("Egg Bun", await AddRoleAsync("Topping"), [(eggId, 150m)]);

        // The eggs are bigger than recorded: 60 g each.
        piece.BaseUnitsPerUnit = 60m;
        await repository.UpdateAsync(piece, Cancellation);

        await using var check = CreateDbContext();
        (await check.SubAssemblyLines.AsNoTracking().SingleAsync(l => l.SubAssemblyId == subAssemblyId, Cancellation)).Grams.Should().Be(150m);
        (await check.SubAssemblyLines.AsNoTracking().SingleAsync(l => l.SubAssemblyId == custardRecipeId, Cancellation)).Grams.Should().Be(100m);
        (await SubAssemblyTotalsAsync(subAssemblyId)).Should().Be((150m, 30m)); // 150 g at 0.20/g
        (await GetCostPerGramAsync(eggId)).Should().Be(0.2m, "a price per gram doesn't depend on the piece's weight");
        (await GetCostPerGramAsync(custardId)).Should().Be(0.2m);
    }

    [Test]
    public async Task DeleteAsync_removes_an_ingredients_unit_even_while_recipes_use_the_ingredient()
    {
        var eggId = await SeedIngredientAsync("Egg");
        var pieceUnitOfMeasureId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = eggId, UnitOfMeasureId = pieceUnitOfMeasureId, BaseUnitsPerUnit = 50m }, Cancellation);
        var piece = (await repository.GetForRawMaterialAsync(eggId, Cancellation)).Single();
        await AddMadeIngredientAsync("Custard Base", [(eggId, 100m)]);

        await repository.DeleteAsync(piece.Id, Cancellation);

        (await repository.GetForRawMaterialAsync(eggId, Cancellation)).Should().BeEmpty("recipe lines are in grams, never in one of its units");
    }

    [Test]
    public async Task Correcting_a_multiplier_the_latest_price_is_not_in_leaves_the_cost_alone()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = packId, BaseUnitsPerUnit = 1000m }, Cancellation);
        var units = await repository.GetForRawMaterialAsync(flourId, Cancellation);
        var bag = units.Single(u => u.UnitOfMeasureId == bagId);
        var pack = units.Single(u => u.UnitOfMeasureId == packId);
        await SetPriceAsync(flourId, bag.Id, 1250m);

        pack.BaseUnitsPerUnit = 500m;
        await repository.UpdateAsync(pack, Cancellation);

        (await GetCostPerGramAsync(flourId)).Should().Be(0.05m);
    }

    [Test]
    public async Task UpdateAsync_can_rename_the_unit_and_description_but_not_to_a_unit_the_ingredient_already_has()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = packId, BaseUnitsPerUnit = 1000m }, Cancellation);
        var bag = (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Single(u => u.UnitOfMeasureId == bagId);

        bag.UnitOfMeasureId = sackId;
        bag.Description = "25 kg sack";
        await repository.UpdateAsync(bag, Cancellation);

        var renamed = (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Single(u => u.Id == bag.Id);
        renamed.UnitOfMeasure!.Name.Should().Be("sack");
        renamed.Description.Should().Be("25 kg sack");

        renamed.UnitOfMeasureId = packId;
        var act = () => repository.UpdateAsync(renamed, Cancellation);
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task DeleteAsync_removes_an_unused_unit()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);
        var bag = (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Single();

        await repository.DeleteAsync(bag.Id, Cancellation);

        (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task DeleteAsync_rejects_a_unit_a_price_is_recorded_in()
    {
        var flourId = await SeedIngredientAsync("Flour");
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = flourId, UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m }, Cancellation);
        var bag = (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Single();
        await SetPriceAsync(flourId, bag.Id, 1250m);

        var act = () => repository.DeleteAsync(bag.Id, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'bag' is used by 1 purchase price*");
        (await repository.GetForRawMaterialAsync(flourId, Cancellation)).Should().ContainSingle();
    }

    [Test]
    public async Task DeleteAsync_rejects_a_nonexistent_unit()
    {
        var act = () => CreateRepository().DeleteAsync(999_999, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
