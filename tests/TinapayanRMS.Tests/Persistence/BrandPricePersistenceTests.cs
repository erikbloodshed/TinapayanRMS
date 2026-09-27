using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class BrandPricePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Today);

    /// <summary>A purchased flour with a 25,000 g bag and a 1,000 g pack; returns the ingredient's and both units' ids.</summary>
    private async Task<(int IngredientId, int BagId, int PackId)> SeedPurchasedIngredientAsync()
    {
        var bagId = await GetOrCreateUnitOfMeasureAsync("bag", "bag");
        var packId = await GetOrCreateUnitOfMeasureAsync("pack", "pack");

        await using var dbContext = CreateDbContext();
        var bag = new RawMaterialUnit { UnitOfMeasureId = bagId, BaseUnitsPerUnit = 25000m };
        var pack = new RawMaterialUnit { UnitOfMeasureId = packId, BaseUnitsPerUnit = 1000m };
        var ingredient = new RawMaterial
        {
            Name = "All-Purpose Flour",
            Category = new RawMaterialCategory { Name = "Flour" },
            Source = RawMaterialSource.Purchased,
            Units = [bag, pack],
        };
        dbContext.RawMaterials.Add(ingredient);
        await dbContext.SaveChangesAsync(Cancellation);
        return (ingredient.Id, bag.Id, pack.Id);
    }

    /// <summary>A satin ribbon measured in cm, bought by the 1,000 cm roll; returns the material's and the roll's ids.</summary>
    private async Task<(int PackagingMaterialId, int RollId)> SeedPackagingMaterialAsync()
    {
        var centimeterId = await GetOrCreateUnitOfMeasureAsync("centimeter", "cm");
        var rollUnitOfMeasureId = await GetOrCreateUnitOfMeasureAsync("roll", "roll");

        await using var dbContext = CreateDbContext();
        var roll = new RawMaterialUnit { UnitOfMeasureId = rollUnitOfMeasureId, BaseUnitsPerUnit = 1000m };
        var packagingMaterial = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Satin Ribbon",
            Category = new RawMaterialCategory { Name = "Ribbons" },
            BaseUnitId = centimeterId,
            Units = [roll],
        };
        dbContext.RawMaterials.Add(packagingMaterial);
        await dbContext.SaveChangesAsync(Cancellation);
        return (packagingMaterial.Id, roll.Id);
    }

    private async Task<RawMaterial> GetMaterialAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == id, Cancellation);
    }

    private async Task<List<RawMaterialPriceHistory>> GetHistoryAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterialPriceHistories.AsNoTracking().Where(h => h.RawMaterialId == id).OrderBy(h => h.Id).ToListAsync(Cancellation);
    }

    private static RawMaterialBrandPrice Brand(string? brand, int? unitId, decimal price) => new() { Brand = brand, UnitId = unitId, Price = price };

    [Fact]
    public async Task A_single_brand_prices_the_ingredient_per_gram_through_its_unit()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();

        await SetPriceAsync(ingredientId, bagId, 900m);

        (await GetMaterialAsync(ingredientId)).CostPerBaseUnit.Should().Be(0.036m); // 900 / 25,000 g
    }

    [Fact]
    public async Task A_price_with_no_unit_is_a_price_per_base_unit()
    {
        var (packagingMaterialId, _) = await SeedPackagingMaterialAsync();

        await SetPriceAsync(packagingMaterialId, null, 0.05m);

        (await GetMaterialAsync(packagingMaterialId)).CostPerBaseUnit.Should().Be(0.05m);
    }

    [Fact]
    public async Task A_packaging_material_is_priced_per_base_unit_through_its_unit()
    {
        var (packagingMaterialId, rollId) = await SeedPackagingMaterialAsync();

        await SetPriceAsync(packagingMaterialId, rollId, 45m);

        (await GetMaterialAsync(packagingMaterialId)).CostPerBaseUnit.Should().Be(0.045m);
    }

    [Fact]
    public async Task The_ingredient_is_costed_at_the_dearest_brand_per_gram_whatever_units_they_are_in()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();

        // ₱900 a 25 kg bag is ₱0.036/g; ₱40 a 1 kg pack is ₱0.04/g, dearer despite the lower price.
        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Gold Medal", bagId, 900m), Brand("Wellington", packId, 40m)], Cancellation);

        (await GetMaterialAsync(ingredientId)).CostPerBaseUnit.Should().Be(0.04m);
        var saved = await CreateBrandPriceRepository().GetForRawMaterialAsync(ingredientId, Cancellation);
        saved.Select(p => (p.Brand, p.Unit!.UnitOfMeasure!.Name, p.Price)).Should().Equal(("Gold Medal", "bag", 900m), ("Wellington", "pack", 40m));
    }

    [Fact]
    public async Task A_new_cost_logs_the_dearest_brands_price_dated_today()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();

        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Gold Medal", bagId, 900m), Brand("Wellington", packId, 40m)], Cancellation);

        var entry = (await GetHistoryAsync(ingredientId)).Should().ContainSingle().Subject;
        (entry.Date, entry.Brand, entry.UnitId, entry.Price).Should().Be((Today, "Wellington", packId, 40m));
    }

    [Fact]
    public async Task Saving_brands_that_leave_the_cost_as_it_was_logs_nothing()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();
        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Wellington", packId, 40m)], Cancellation);

        // A cheaper brand doesn't move the cost.
        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Wellington", packId, 40m), Brand("Gold Medal", bagId, 900m)], Cancellation);

        (await GetHistoryAsync(ingredientId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Removing_the_dearest_brand_lowers_the_cost_and_logs_the_next_dearest()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();
        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Gold Medal", bagId, 900m), Brand("Wellington", packId, 40m)], Cancellation);

        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Gold Medal", bagId, 900m)], Cancellation);

        (await GetMaterialAsync(ingredientId)).CostPerBaseUnit.Should().Be(0.036m);
        (await GetHistoryAsync(ingredientId)).Select(h => (h.Brand, h.Price)).Should().Equal(("Wellington", 40m), ("Gold Medal", 900m));
    }

    [Fact]
    public async Task Removing_every_brand_makes_the_material_unpriced_and_logs_nothing()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        await SetPriceAsync(ingredientId, bagId, 900m);

        await CreateBrandPriceRepository().SetAsync(ingredientId, [], Cancellation);

        var material = await GetMaterialAsync(ingredientId);
        (material.CostPerBaseUnit, material.BufferedCostPerBaseUnit).Should().Be((0m, 0m));
        (await GetHistoryAsync(ingredientId)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_brand_is_saved_trimmed_and_an_empty_one_as_none()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();

        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("  Gold Medal ", bagId, 900m), Brand("   ", packId, 30m)], Cancellation);

        var saved = await CreateBrandPriceRepository().GetForRawMaterialAsync(ingredientId, Cancellation);
        saved.Select(p => p.Brand).Should().Equal("Gold Medal", null);
    }

    [Fact]
    public async Task The_same_brand_can_be_priced_in_two_units()
    {
        var (ingredientId, bagId, packId) = await SeedPurchasedIngredientAsync();

        await CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Gold Medal", bagId, 900m), Brand("Gold Medal", packId, 40m)], Cancellation);

        (await CreateBrandPriceRepository().GetForRawMaterialAsync(ingredientId, Cancellation)).Should().HaveCount(2);
    }

    [Fact]
    public async Task SetAsync_rejects_a_brand_priced_twice_in_the_same_unit_whatever_its_case()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();

        var act = () => CreateBrandPriceRepository().SetAsync(ingredientId, [Brand("Gold Medal", bagId, 900m), Brand("gold medal", bagId, 950m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*priced twice*");
        (await CreateBrandPriceRepository().GetForRawMaterialAsync(ingredientId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task SetAsync_rejects_a_non_positive_price()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();

        var act = () => SetPriceAsync(ingredientId, bagId, 0m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*positive*");
    }

    [Fact]
    public async Task SetAsync_rejects_a_unit_that_belongs_to_another_material()
    {
        var (ingredientId, _, _) = await SeedPurchasedIngredientAsync();
        var (_, rollId) = await SeedPackagingMaterialAsync();

        var act = () => SetPriceAsync(ingredientId, rollId, 45m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*own units*");
    }

    [Fact]
    public async Task SetAsync_rejects_prices_for_a_made_ingredient()
    {
        await using var dbContext = CreateDbContext();
        var syrup = new RawMaterial
        {
            Name = "Simple Syrup",
            Category = new RawMaterialCategory { Name = "Fillings" },
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
        };
        dbContext.RawMaterials.Add(syrup);
        await dbContext.SaveChangesAsync(Cancellation);

        var act = () => SetPriceAsync(syrup.Id, null, 0.05m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*purchased*");
    }

    [Fact]
    public async Task SetAsync_rejects_a_nonexistent_material()
    {
        var act = () => SetPriceAsync(999_999, null, 0.05m);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task The_price_history_lists_newest_first_with_each_entrys_unit()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        await SetPriceAsync(ingredientId, null, 0.05m);
        await SetPriceAsync(ingredientId, bagId, 900m);

        var history = await new RawMaterialPriceHistoryRepository(CreateDbContextFactory()).GetForRawMaterialAsync(ingredientId, Cancellation);

        history.Select(h => (h.Date, h.Price, h.Unit?.UnitOfMeasure?.Name)).Should().Equal((Today, 900m, "bag"), (Today, 0.05m, null));
    }

    [Fact]
    public async Task The_whole_price_history_lists_each_materials_entries_together_newest_first()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        var (ribbonId, rollId) = await SeedPackagingMaterialAsync();
        await SetPriceAsync(ingredientId, null, 0.05m);
        await SetPriceAsync(ribbonId, rollId, 120m);
        await SetPriceAsync(ingredientId, bagId, 900m);

        var history = await new RawMaterialPriceHistoryRepository(CreateDbContextFactory()).GetAllAsync(Cancellation);

        history.Select(h => (h.RawMaterialId, h.Price, h.Unit?.UnitOfMeasure?.Name)).Should().Equal(
            (ingredientId, 900m, "bag"), (ingredientId, 0.05m, null), (ribbonId, 120m, "roll"));
    }

    [Fact]
    public async Task Deleting_a_price_history_entry_leaves_the_cost_as_it_is()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        await SetPriceAsync(ingredientId, bagId, 900m);
        var entry = (await GetHistoryAsync(ingredientId)).Single();

        await new RawMaterialPriceHistoryRepository(CreateDbContextFactory()).DeleteAsync(entry.Id, Cancellation);

        (await GetHistoryAsync(ingredientId)).Should().BeEmpty();
        (await GetMaterialAsync(ingredientId)).CostPerBaseUnit.Should().Be(0.036m);
    }

    [Fact]
    public async Task Deleting_a_nonexistent_price_history_entry_is_rejected()
    {
        var act = () => new RawMaterialPriceHistoryRepository(CreateDbContextFactory()).DeleteAsync(999_999, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_material_cascades_to_its_brand_prices_and_price_history()
    {
        var (ingredientId, bagId, _) = await SeedPurchasedIngredientAsync();
        await SetPriceAsync(ingredientId, bagId, 900m);

        await new RawMaterialRepository(CreateDbContextFactory(), CreateCostPropagator()).DeleteAsync(ingredientId, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterialBrandPrices.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    }
}
