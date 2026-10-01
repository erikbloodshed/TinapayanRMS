using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// RawMaterialRepository.SaveAsync, the Raw Materials screen's one Save: a material with its alternative
/// units, a new price and its BOM, written in one transaction.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class RawMaterialSavePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    private IngredientBomLineRepository CreateBomRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

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

    private static RawMaterial MadeIngredient(string name, int categoryId, decimal weightLossPercent, int id = 0) =>
        new() { Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Made, WeightLossPercent = weightLossPercent };

    private static RawMaterialUnit Unit(int unitOfMeasureId, decimal baseUnitsPerUnit, int id = 0) =>
        new() { Id = id, UnitOfMeasureId = unitOfMeasureId, BaseUnitsPerUnit = baseUnitsPerUnit };

    [Fact]
    public async Task A_new_purchased_ingredient_is_saved_with_its_units_and_a_price_in_one_of_them()
    {
        var categoryId = await SeedCategoryAsync();
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var flour = PurchasedIngredient("Flour", categoryId);

        var flourId = await CreateRepository().SaveAsync(flour, [Unit(bagId, 25000m), Unit(packId, 1000m)], [(null, bagId, 1250m)], bomLines: null, Cancellation);

        flour.Id.Should().Be(0, "the caller's entity is only read, never attached");
        var saved = await CreateRepository().GetByIdAsync(flourId, Cancellation);
        saved!.Units.Select(u => (u.UnitOfMeasureId, u.BaseUnitsPerUnit)).Should().Equal((bagId, 25000m), (packId, 1000m));
        saved.CostPerBaseUnit.Should().Be(0.05m, "₱1,250 a 25,000 g bag");

        await using var dbContext = CreateDbContext();
        var price = await dbContext.RawMaterialPriceHistories.AsNoTracking().SingleAsync(Cancellation);
        price.UnitId.Should().Be(saved.Units[0].Id, "the price is in the bag added by the same save");
        price.Date.Should().Be(Today, "a new cost is logged the day it's saved");
        price.Price.Should().Be(1250m);
    }

    [Fact]
    public async Task A_new_packaging_material_is_saved_with_its_base_unit_a_unit_and_a_price()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var ribbon = new RawMaterial { Name = "Satin Ribbon", Type = RawMaterialType.Packaging, CategoryId = await AddPackagingCategoryAsync(), BaseUnitId = centimeterId };

        var ribbonId = await CreateRepository().SaveAsync(ribbon, [Unit(rollId, 1000m)], [(null, rollId, 45m)], bomLines: null, Cancellation);

        var saved = await CreateRepository().GetByIdAsync(ribbonId, Cancellation);
        saved!.BaseUnit!.Abbreviation.Should().Be("cm");
        saved.Units.Should().ContainSingle().Which.BaseUnitsPerUnit.Should().Be(1000m);
        saved.CostPerBaseUnit.Should().Be(0.045m, "₱45 a 1,000 cm roll");
    }

    [Fact]
    public async Task A_new_made_ingredient_is_saved_with_its_bom_and_costed_from_it()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], [(null, null, 0.09m)], bomLines: null, Cancellation);

        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId, 20m), [], brandPrices: null, [(sugarId, 1000m)], Cancellation);

        (await repository.GetByIdAsync(syrupId, Cancellation))!.CostPerBaseUnit
            .Should().Be(0.1125m, "₱90 of sugar over the 800 g left after 20% weight loss");
        (await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation)).Should().ContainSingle().Which.Grams.Should().Be(1000m);
    }

    [Fact]
    public async Task Nothing_is_saved_when_the_bom_is_rejected_after_the_material_and_its_units()
    {
        var categoryId = await SeedCategoryAsync();
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();
        var boxId = await repository.SaveAsync(
            new RawMaterial { Name = "Cake Box", Type = RawMaterialType.Packaging, CategoryId = await AddPackagingCategoryAsync(), BaseUnitId = pieceId },
            [], brandPrices: null, bomLines: null, Cancellation);

        var act = () => repository.SaveAsync(MadeIngredient("Custard", categoryId, 0m), [Unit(bagId, 25000m)], brandPrices: null, [(boxId, 1m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'Cake Box' is packaging*");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.Select(m => m.Name).ToListAsync(Cancellation)).Should().Equal("Cake Box");
        (await dbContext.RawMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Nothing_is_saved_when_the_price_is_in_a_unit_the_material_does_not_have()
    {
        var categoryId = await SeedCategoryAsync();
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var sackId = await SeedUnitOfMeasureAsync("sack", "sk");

        var act = () => CreateRepository().SaveAsync(PurchasedIngredient("Flour", categoryId), [Unit(bagId, 25000m)], [(null, sackId, 900m)], bomLines: null, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*own units*");
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task An_update_renames_corrects_a_multiplier_removes_a_unit_and_adds_one()
    {
        var categoryId = await SeedCategoryAsync();
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var cupId = await SeedUnitOfMeasureAsync("cup", "cup");
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [Unit(bagId, 1000m), Unit(packId, 500m)], [(null, bagId, 90m)], bomLines: null, Cancellation);
        var bag = (await repository.GetByIdAsync(sugarId, Cancellation))!.Units.Single(u => u.UnitOfMeasureId == bagId);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId, 0m), [], brandPrices: null, [(sugarId, 1000m)], Cancellation);

        // The bag really holds 900 g; the pack goes and a cup comes in.
        await repository.SaveAsync(
            PurchasedIngredient("Refined Sugar", categoryId, sugarId), [Unit(bagId, 900m, bag.Id), Unit(cupId, 200m)], brandPrices: null, bomLines: null, Cancellation);

        var sugar = await repository.GetByIdAsync(sugarId, Cancellation);
        sugar!.Name.Should().Be("Refined Sugar");
        sugar.Units.Select(u => (u.Id == bag.Id, u.UnitOfMeasureId, u.BaseUnitsPerUnit)).Should().Equal((true, bagId, 900m), (false, cupId, 200m));
        sugar.CostPerBaseUnit.Should().Be(0.1m, "₱90 a bag that holds 900 g");
        (await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation)).Single().Grams.Should().Be(1000m, "a recipe line is in grams, whatever a bag holds");
        (await repository.GetByIdAsync(syrupId, Cancellation))!.CostPerBaseUnit.Should().Be(0.1m, "the made ingredient using sugar is recomputed");
    }

    [Fact]
    public async Task Removing_a_unit_a_price_is_in_is_rejected_and_undoes_the_rest_of_the_save()
    {
        var categoryId = await SeedCategoryAsync();
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var repository = CreateRepository();
        var flourId = await repository.SaveAsync(PurchasedIngredient("Flour", categoryId), [Unit(bagId, 25000m)], [(null, bagId, 1250m)], bomLines: null, Cancellation);

        var act = () => repository.SaveAsync(PurchasedIngredient("Bread Flour", categoryId, flourId), [], brandPrices: null, bomLines: null, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'bag' is used by 1 purchase price*");
        var flour = await repository.GetByIdAsync(flourId, Cancellation);
        flour!.Name.Should().Be("Flour", "the rename was part of the save that was rejected");
        flour.Units.Should().ContainSingle();
    }

    [Fact]
    public async Task The_units_are_checked_as_the_save_leaves_them()
    {
        var categoryId = await SeedCategoryAsync();
        var bagId = await SeedUnitOfMeasureAsync("bag", "bag");
        var packId = await SeedUnitOfMeasureAsync("pack", "pack");
        var cupId = await SeedUnitOfMeasureAsync("cup", "cup");
        var repository = CreateRepository();

        // Any number of alternative units.
        var flourId = await repository.SaveAsync(
            PurchasedIngredient("Flour", categoryId), [Unit(bagId, 25000m), Unit(packId, 1000m), Unit(cupId, 120m)], brandPrices: null, bomLines: null, Cancellation);
        (await repository.GetByIdAsync(flourId, Cancellation))!.Units.Should().HaveCount(3);

        // Saved units replaced, the bag by a new row: removals go first, so the one-row-per-unit check doesn't trip.
        await repository.SaveAsync(PurchasedIngredient("Flour", categoryId, flourId), [Unit(bagId, 50000m), Unit(cupId, 120m)], brandPrices: null, bomLines: null, Cancellation);

        (await repository.GetByIdAsync(flourId, Cancellation))!.Units.Select(u => (u.UnitOfMeasureId, u.BaseUnitsPerUnit))
            .Should().Equal((bagId, 50000m), (cupId, 120m));
    }

    [Fact]
    public async Task Saving_without_bom_lines_leaves_a_made_ingredients_bom_alone()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = CreateRepository();
        var sugarId = await repository.SaveAsync(PurchasedIngredient("Sugar", categoryId), [], [(null, null, 0.09m)], bomLines: null, Cancellation);
        var syrupId = await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId, 20m), [], brandPrices: null, [(sugarId, 1000m)], Cancellation);
        var lineId = (await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation)).Single().Id;

        await repository.SaveAsync(MadeIngredient("Simple Syrup", categoryId, 10m, syrupId), [], brandPrices: null, bomLines: null, Cancellation);

        (await CreateBomRepository().GetForIngredientAsync(syrupId, Cancellation)).Should().ContainSingle()
            .Which.Id.Should().Be(lineId, "the line wasn't replaced");
        (await repository.GetByIdAsync(syrupId, Cancellation))!.CostPerBaseUnit
            .Should().Be(0.1m, "recomputed from the BOM it kept: ₱90 over the 900 g left after 10% weight loss");
    }
}
