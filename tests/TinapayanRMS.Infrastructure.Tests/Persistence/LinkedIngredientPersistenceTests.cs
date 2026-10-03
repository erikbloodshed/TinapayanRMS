using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A linked ingredient takes a share of another ingredient's cost: an egg bought by the 1,500 g tray at ₱135
/// is ₱0.09 a gram, and egg white (70% of its weight, 30% of its cost) and egg yolk (30%, 70%) are costed from
/// it, follow it through the cascade, and together can't take more than all of it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class LinkedIngredientPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    /// <summary>Stored costs keep 4 decimal places, so a share that doesn't divide evenly is compared to within one of them.</summary>
    private const decimal StoredPrecision = 0.0001m;

    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Eggs" };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private static RawMaterial Purchased(string name, int categoryId, decimal costBufferPercent = 0m, int id = 0) =>
        new() { Id = id, Name = name, Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Purchased, CostBufferPercent = costBufferPercent };

    private static RawMaterial Linked(
        string name, int categoryId, int linkedToId, decimal weightShare, decimal costShare, decimal costBufferPercent = 0m, int id = 0) =>
        new()
        {
            Id = id,
            Name = name,
            Type = RawMaterialType.Ingredient,
            CategoryId = categoryId,
            Source = RawMaterialSource.Linked,
            LinkedToId = linkedToId,
            WeightSharePercent = weightShare,
            CostSharePercent = costShare,
            CostBufferPercent = costBufferPercent,
        };

    /// <summary>An egg bought by the tray: 1,500 g for <paramref name="price"/>.</summary>
    private async Task<int> SaveEggAsync(int categoryId, decimal price = 135m, decimal costBufferPercent = 0m)
    {
        var trayId = await SeedUnitOfMeasureAsync("tray", "tray");
        return await CreateRepository().SaveAsync(
            Purchased("Egg", categoryId, costBufferPercent),
            [new RawMaterialUnit { UnitOfMeasureId = trayId, BaseUnitsPerUnit = 1500m }],
            [(null, trayId, price)],
            bomLines: null,
            Cancellation);
    }

    private Task<int> SaveAsync(RawMaterial material, IReadOnlyList<(int ComponentIngredientId, decimal Grams)>? bomLines = null) =>
        CreateRepository().SaveAsync(material, [], brandPrices: null, bomLines, Cancellation);

    private async Task<RawMaterial> GetMaterialAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == id, Cancellation);
    }

    private async Task<decimal> GetSubAssemblyTotalCostAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return (await dbContext.SubAssemblies.AsNoTracking().SingleAsync(s => s.Id == id, Cancellation)).TotalCost;
    }

    [Fact]
    public async Task Egg_white_and_yolk_are_costed_from_the_egg_by_their_shares()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);

        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));
        var yolkId = await SaveAsync(Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m));

        (await GetMaterialAsync(eggId)).CostPerBaseUnit.Should().Be(0.09m);

        var white = await GetMaterialAsync(whiteId);
        white.CostPerBaseUnit.Should().BeApproximately(0.0385714m, StoredPrecision, "₱0.09 × 30 / 70");
        white.BufferedCostPerBaseUnit.Should().Be(white.CostPerBaseUnit, "nothing has a buffer");

        var yolk = await GetMaterialAsync(yolkId);
        yolk.CostPerBaseUnit.Should().Be(0.21m, "₱0.09 × 70 / 30");
        yolk.BufferedCostPerBaseUnit.Should().Be(0.21m);
    }

    [Fact]
    public async Task Adding_a_linked_ingredient_through_AddAsync_costs_it_straight_away()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var yolk = Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m);

        await CreateRepository().AddAsync(yolk, Cancellation);

        (await GetMaterialAsync(yolk.Id)).CostPerBaseUnit.Should().Be(0.21m);
    }

    [Fact]
    public async Task Repricing_the_egg_moves_its_parts_and_the_sub_assemblies_using_them()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));
        var yolkId = await SaveAsync(Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m));
        var roleId = await AddRoleAsync("Filling");
        var (_, custardId) = await AddProductWithSubAssemblyAsync("Custard Bun", roleId, [(yolkId, 450m)]);

        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(94.50m, "450 g of yolk is 70% of a ₱135 tray");

        // ₱180 a tray is ₱0.12 a gram.
        var trayUnitId = await TrayUnitOfMeasureIdAsync();
        await CreateRepository().SaveAsync(Purchased("Egg", categoryId, id: eggId), await UnitsOfAsync(eggId), [(null, trayUnitId, 180m)], bomLines: null, Cancellation);

        (await GetMaterialAsync(whiteId)).CostPerBaseUnit.Should().BeApproximately(0.0514286m, StoredPrecision, "₱0.12 × 30 / 70");
        (await GetMaterialAsync(yolkId)).CostPerBaseUnit.Should().Be(0.28m, "₱0.12 × 70 / 30");
        (await GetSubAssemblyTotalCostAsync(custardId)).Should().Be(126m, "450 g of yolk is 70% of a ₱180 tray");
    }

    [Fact]
    public async Task A_part_inherits_the_eggs_buffer_percent_on_top_of_its_share_of_the_eggs_unbuffered_cost()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId, costBufferPercent: 10m);

        // A buffer given to the part itself is ignored: it takes the egg's.
        var yolkId = await SaveAsync(Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m, costBufferPercent: 20m));
        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));

        (await GetMaterialAsync(eggId)).BufferedCostPerBaseUnit.Should().Be(0.099m);

        var yolk = await GetMaterialAsync(yolkId);
        yolk.CostBufferPercent.Should().Be(10m, "inherited from the egg, not the 20% it was given");
        yolk.CostPerBaseUnit.Should().Be(0.21m, "70% of the egg's unbuffered ₱0.09, over 30% of its weight");
        yolk.BufferedCostPerBaseUnit.Should().Be(0.231m, "₱0.21 with the egg's 10%");

        var white = await GetMaterialAsync(whiteId);
        white.CostBufferPercent.Should().Be(10m);
        white.BufferedCostPerBaseUnit.Should().BeApproximately(0.0424286m, StoredPrecision, "₱0.0385714 with the egg's 10%");

        // A buffer change on the egg reaches its parts.
        await CreateRepository().SaveAsync(Purchased("Egg", categoryId, costBufferPercent: 50m, id: eggId), await UnitsOfAsync(eggId), brandPrices: null, bomLines: null, Cancellation);
        yolk = await GetMaterialAsync(yolkId);
        yolk.CostBufferPercent.Should().Be(50m);
        yolk.BufferedCostPerBaseUnit.Should().Be(0.315m, "₱0.21 with the egg's new 50%");

        // Resaving the part with a buffer of its own still leaves it on the egg's.
        await SaveAsync(Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m, costBufferPercent: 5m, id: yolkId));
        (await GetMaterialAsync(yolkId)).CostBufferPercent.Should().Be(50m);
    }

    [Fact]
    public async Task A_part_takes_its_share_of_the_eggs_fixed_buffered_cost_not_of_a_new_price()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId, costBufferPercent: 10m);
        var yolkId = await SaveAsync(Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m));

        // The egg goes up to ₱0.12/g; its buffered cost stays at ₱0.099/g until it's recalculated.
        await SetPriceAsync(eggId, null, 0.12m);
        var yolk = await GetMaterialAsync(yolkId);
        yolk.CostPerBaseUnit.Should().Be(0.28m, "70% of the egg's new ₱0.12, over 30% of its weight");
        yolk.BufferedCostPerBaseUnit.Should().Be(0.231m, "the same share of the egg's unchanged ₱0.099");

        // A buffered cost typed on the egg reaches the yolk as the same share of it.
        var egg = Purchased("Egg", categoryId, costBufferPercent: 10m, id: eggId);
        egg.BufferedCostPerBaseUnit = 0.15m;
        await CreateRepository().SaveAsync(egg, await UnitsOfAsync(eggId), brandPrices: null, bomLines: null, Cancellation);
        (await GetMaterialAsync(yolkId)).BufferedCostPerBaseUnit.Should().Be(0.35m, "₱0.15 × 70 / 30");
    }

    [Fact]
    public async Task The_buffer_is_inherited_on_AddAsync_down_a_chain_of_links_and_from_a_new_link()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId, costBufferPercent: 10m);
        var yolkId = await SaveAsync(Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m));

        // A part of a part: half of the yolk's weight for half of its cost.
        var yolkHalf = Linked("Half Yolk", categoryId, yolkId, weightShare: 50m, costShare: 50m, costBufferPercent: 0m);
        await CreateRepository().AddAsync(yolkHalf, Cancellation);

        var half = await GetMaterialAsync(yolkHalf.Id);
        half.CostBufferPercent.Should().Be(10m, "the yolk's, which is the egg's");
        half.BufferedCostPerBaseUnit.Should().Be(0.231m);

        await CreateRepository().SaveAsync(Purchased("Egg", categoryId, costBufferPercent: 20m, id: eggId), await UnitsOfAsync(eggId), brandPrices: null, bomLines: null, Cancellation);
        (await GetMaterialAsync(yolkHalf.Id)).CostBufferPercent.Should().Be(20m, "an egg buffer change reaches every link down the chain");

        // Relinked to a duck egg with no buffer, the yolk takes the duck egg's.
        var duckEggId = await SaveAsync(Purchased("Duck Egg", categoryId));
        await SaveAsync(Linked("Egg Yolk", categoryId, duckEggId, weightShare: 30m, costShare: 70m, id: yolkId));
        (await GetMaterialAsync(yolkId)).CostBufferPercent.Should().Be(0m);
        (await GetMaterialAsync(yolkHalf.Id)).CostBufferPercent.Should().Be(0m);
    }

    [Fact]
    public async Task Changing_a_parts_shares_or_link_reprices_it()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var duckEggId = await SaveAsync(Purchased("Duck Egg", categoryId));
        await SetPriceAsync(duckEggId, null, 0.12m);
        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));

        await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 60m, costShare: 40m, id: whiteId));
        (await GetMaterialAsync(whiteId)).CostPerBaseUnit.Should().Be(0.06m, "₱0.09 × 40 / 60");

        await SaveAsync(Linked("Egg White", categoryId, duckEggId, weightShare: 60m, costShare: 40m, id: whiteId));
        (await GetMaterialAsync(whiteId)).CostPerBaseUnit.Should().Be(0.08m, "₱0.12 × 40 / 60");
    }

    [Fact]
    public async Task A_part_that_takes_the_egg_past_all_of_its_weight_is_refused_naming_the_other_parts()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));
        await SaveAsync(Linked("Egg Yolk", categoryId, eggId, weightShare: 30m, costShare: 70m));

        var shell = () => SaveAsync(Linked("Egg Shell", categoryId, eggId, weightShare: 10m, costShare: 1m));

        await shell.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Egg White (70%), Egg Yolk (30%) already take 100% of Egg's weight, so Egg Shell can take at most 0%.");
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.RawMaterials.AnyAsync(m => m.Name == "Egg Shell", Cancellation)).Should().BeFalse("the save rolled back");
        }

        // A part never counts against itself: saving egg white again at its own shares still fits.
        var resave = () => SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m, id: whiteId));
        await resave.Should().NotThrowAsync();
    }

    [Fact]
    public async Task A_link_to_itself_or_to_packaging_is_refused()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var trayBoxId = await SaveAsync(new RawMaterial
        {
            Name = "Egg Tray Box",
            Type = RawMaterialType.Packaging,
            CategoryId = await AddPackagingCategoryAsync(),
            Source = RawMaterialSource.Purchased,
            BaseUnitId = pieceId,
        });

        var toItself = () => SaveAsync(Linked("Egg White", categoryId, whiteId, weightShare: 70m, costShare: 30m, id: whiteId));
        var toPackaging = () => SaveAsync(Linked("Egg Yolk", categoryId, trayBoxId, weightShare: 30m, costShare: 70m));

        await toItself.Should().ThrowAsync<InvalidOperationException>().WithMessage("*itself*");
        await toPackaging.Should().ThrowAsync<InvalidOperationException>().WithMessage("'Egg Tray Box' is packaging*");
    }

    [Fact]
    public async Task Linking_a_part_to_a_made_ingredient_that_already_uses_it_is_refused_as_a_cycle()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));
        var meringueId = await SaveAsync(
            new RawMaterial { Name = "Meringue Base", Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Made, WeightLossPercent = 0m },
            [(whiteId, 100m)]);

        var act = () => SaveAsync(Linked("Egg White", categoryId, meringueId, weightShare: 70m, costShare: 30m, id: whiteId));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cycle*");
    }

    [Fact]
    public async Task A_bom_using_a_part_linked_to_the_made_ingredient_itself_is_refused_as_a_cycle()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var eggMixId = await SaveAsync(
            new RawMaterial { Name = "Egg Mix", Type = RawMaterialType.Ingredient, CategoryId = categoryId, Source = RawMaterialSource.Made, WeightLossPercent = 0m },
            [(eggId, 100m)]);
        var mixWhiteId = await SaveAsync(Linked("Egg Mix White", categoryId, eggMixId, weightShare: 70m, costShare: 30m));

        var act = () => new IngredientBomLineRepository(CreateDbContextFactory())
            .SetLinesAsync(eggMixId, [(mixWhiteId, 100m)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cycle*");
    }

    [Fact]
    public async Task A_linked_ingredient_takes_no_price()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        var whiteId = await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));

        var act = () => SetPriceAsync(whiteId, null, 10m);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Only a purchased material has purchase prices.");
    }

    [Fact]
    public async Task An_ingredient_with_parts_linked_to_it_cant_be_deleted()
    {
        var categoryId = await SeedCategoryAsync();
        var eggId = await SaveEggAsync(categoryId);
        await SaveAsync(Linked("Egg White", categoryId, eggId, weightShare: 70m, costShare: 30m));

        var act = () => CreateRepository().DeleteAsync(eggId, Cancellation);

        // FK violation surfaces as the driver's own SqlException, not a DbUpdateException.
        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    private async Task<int> TrayUnitOfMeasureIdAsync()
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.UnitsOfMeasure.Where(u => u.Name == "tray").Select(u => u.Id).SingleAsync(Cancellation);
    }

    /// <summary>The material's saved alternative units, so a resave through SaveAsync keeps them.</summary>
    private async Task<IReadOnlyList<RawMaterialUnit>> UnitsOfAsync(int rawMaterialId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterialUnits.AsNoTracking().Where(u => u.RawMaterialId == rawMaterialId).ToListAsync(Cancellation);
    }
}
