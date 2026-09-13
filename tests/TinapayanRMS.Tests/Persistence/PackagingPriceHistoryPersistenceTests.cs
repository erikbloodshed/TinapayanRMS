using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class PackagingPriceHistoryPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialPriceHistoryRepository CreateRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    /// <summary>A satin ribbon measured in cm, bought by the 1,000 cm roll; returns the material's and the roll's ids.</summary>
    private async Task<(int PackagingMaterialId, int RollId)> SeedPackagingMaterialAsync()
    {
        var centimeterId = await GetOrCreateUnitOfMeasureAsync("centimeter", "cm");
        var rollUnitOfMeasureId = await GetOrCreateUnitOfMeasureAsync("roll", "roll");

        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Ribbons" };
        var roll = new RawMaterialUnit { UnitOfMeasureId = rollUnitOfMeasureId, BaseUnitsPerUnit = 1000m };
        var packagingMaterial = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Satin Ribbon",
            Category = category,
            BaseUnitId = centimeterId,
            Units = [roll],
        };
        dbContext.RawMaterials.Add(packagingMaterial);
        await dbContext.SaveChangesAsync(Cancellation);
        return (packagingMaterial.Id, roll.Id);
    }

    private async Task<decimal> GetCostPerBaseUnitAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return (await dbContext.RawMaterials.AsNoTracking().SingleAsync(p => p.Id == id, Cancellation)).CostPerBaseUnit;
    }

    [Fact]
    public async Task Adding_the_first_price_entry_prices_the_material_per_base_unit_through_its_unit()
    {
        var (packagingMaterialId, rollId) = await SeedPackagingMaterialAsync();

        await CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = packagingMaterialId,
            UnitId = rollId,
            Date = new DateOnly(2026, 1, 1),
            Price = 45m,
        }, Cancellation);

        (await GetCostPerBaseUnitAsync(packagingMaterialId)).Should().Be(0.045m);
    }

    [Fact]
    public async Task A_price_with_no_unit_is_a_price_per_base_unit()
    {
        var (packagingMaterialId, _) = await SeedPackagingMaterialAsync();

        await CreateRepository().AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, Date = new DateOnly(2026, 1, 1), Price = 0.05m }, Cancellation);

        (await GetCostPerBaseUnitAsync(packagingMaterialId)).Should().Be(0.05m);
    }

    [Fact]
    public async Task Adding_a_newer_dated_entry_replaces_the_cost()
    {
        var (packagingMaterialId, rollId) = await SeedPackagingMaterialAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, UnitId = rollId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, UnitId = rollId, Date = new DateOnly(2026, 2, 1), Price = 48m }, Cancellation);

        (await GetCostPerBaseUnitAsync(packagingMaterialId)).Should().Be(0.048m);
    }

    [Fact]
    public async Task Adding_a_second_price_on_the_same_date_makes_it_the_current_price()
    {
        var (packagingMaterialId, rollId) = await SeedPackagingMaterialAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, UnitId = rollId, Date = new DateOnly(2026, 9, 13), Price = 45m }, Cancellation);
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, UnitId = rollId, Date = new DateOnly(2026, 9, 13), Price = 50m }, Cancellation);

        (await GetCostPerBaseUnitAsync(packagingMaterialId)).Should().Be(0.05m);
    }

    [Fact]
    public async Task Deleting_the_only_entry_zeroes_the_cost()
    {
        var (packagingMaterialId, rollId) = await SeedPackagingMaterialAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, UnitId = rollId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);
        var entry = (await repository.GetForRawMaterialAsync(packagingMaterialId, Cancellation)).Should().ContainSingle().Subject;

        await repository.DeleteAsync(entry.Id, Cancellation);

        (await GetCostPerBaseUnitAsync(packagingMaterialId)).Should().Be(0m);
    }

    [Fact]
    public async Task AddAsync_rejects_a_non_positive_price()
    {
        var (packagingMaterialId, _) = await SeedPackagingMaterialAsync();

        var act = () => CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = packagingMaterialId,
            Date = new DateOnly(2026, 1, 1),
            Price = -5m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_unit_that_belongs_to_another_material()
    {
        var (packagingMaterialId, _) = await SeedPackagingMaterialAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var packId = await GetOrCreateUnitOfMeasureAsync("pack", "pack");
        int otherPackId;
        await using (var dbContext = CreateDbContext())
        {
            var pack = new RawMaterialUnit { UnitOfMeasureId = packId, BaseUnitsPerUnit = 100m };
            dbContext.RawMaterials.Add(new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Box",
                CategoryId = (await dbContext.RawMaterialCategories.FirstAsync(Cancellation)).Id,
                BaseUnitId = pieceId,
                Units = [pack],
            });
            await dbContext.SaveChangesAsync(Cancellation);
            otherPackId = pack.Id;
        }

        var act = () => CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = packagingMaterialId,
            UnitId = otherPackId,
            Date = new DateOnly(2026, 1, 1),
            Price = 45m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_nonexistent_packaging_material()
    {
        var act = () => CreateRepository().AddAsync(new RawMaterialPriceHistory
        {
            RawMaterialId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 45m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Updating_an_entrys_price_and_unit_recomputes_the_cost()
    {
        var (packagingMaterialId, rollId) = await SeedPackagingMaterialAsync();
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, UnitId = rollId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);
        var entry = (await repository.GetForRawMaterialAsync(packagingMaterialId, Cancellation)).Should().ContainSingle().Subject;

        // Still carries the Unit loaded for display; only the ids and values are saved.
        entry.Price = 0.06m;
        entry.UnitId = null;
        await repository.UpdateAsync(entry, Cancellation);

        (await GetCostPerBaseUnitAsync(packagingMaterialId)).Should().Be(0.06m);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_entry_for_a_nonexistent_packaging_material()
    {
        var act = () => CreateRepository().UpdateAsync(new RawMaterialPriceHistory
        {
            Id = 999_999,
            RawMaterialId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 45m,
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
    public async Task Deleting_a_packaging_material_cascades_to_its_price_history()
    {
        var (packagingMaterialId, rollId) = await SeedPackagingMaterialAsync();
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialPriceHistory { RawMaterialId = packagingMaterialId, UnitId = rollId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.RawMaterials.Where(p => p.Id == packagingMaterialId).ExecuteDeleteAsync(Cancellation);
        }

        (await repository.GetForRawMaterialAsync(packagingMaterialId, Cancellation)).Should().BeEmpty();
    }
}
