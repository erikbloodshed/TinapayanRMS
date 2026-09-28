using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class PackagingMaterialUnitPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialUnitRepository CreateRepository() => new(CreateDbContextFactory(), CreateCostPropagator());

    /// <summary>A satin ribbon with a centimeter base unit; returns its id and the centimeter's.</summary>
    private async Task<(int PackagingMaterialId, int CentimeterId)> SeedRibbonAsync()
    {
        var centimeterId = await GetOrCreateUnitOfMeasureAsync("centimeter", "cm");

        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Satin Ribbon",
            Category = new RawMaterialCategory { Name = "Ribbons" },
            BaseUnitId = centimeterId,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return (material.Id, centimeterId);
    }

    private async Task<decimal> GetCostPerBaseUnitAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return (await dbContext.RawMaterials.AsNoTracking().SingleAsync(m => m.Id == id, Cancellation)).CostPerBaseUnit;
    }

    [Fact]
    public async Task AddAsync_and_GetForPackagingMaterialAsync_round_trip_units_largest_first()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var meterId = await SeedUnitOfMeasureAsync("meter", "m");
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = meterId, BaseUnitsPerUnit = 100m }, Cancellation);
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m, Description = "10 m roll" }, Cancellation);

        var units = await repository.GetForRawMaterialAsync(ribbonId, Cancellation);
        units.Select(u => (u.UnitOfMeasure!.Name, u.BaseUnitsPerUnit, u.Description))
            .Should().Equal(("roll", 1000m, "10 m roll"), ("meter", 100m, null));
    }

    [Fact]
    public async Task A_standard_unit_of_the_base_units_kind_takes_its_fixed_multiplier_and_any_other_keeps_the_one_typed()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var inchId = await SeedUnitOfMeasureAsync("inch", "in");
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = inchId, BaseUnitsPerUnit = 3m }, Cancellation);
        (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Single().BaseUnitsPerUnit.Should().Be(2.54m, "an inch is 2.54 cm");

        // Wrappers counted in pieces, bought by the kilogram: a kilogram's worth of pieces is the wrapper's own.
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var kilogramId = await SeedUnitOfMeasureAsync("kilogram", "kg");
        int wrapperId;
        await using (var dbContext = CreateDbContext())
        {
            var wrapper = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Cello Sheet Wrapper",
                Category = new RawMaterialCategory { Name = "Wrappers" },
                BaseUnitId = pieceId,
            };
            dbContext.RawMaterials.Add(wrapper);
            await dbContext.SaveChangesAsync(Cancellation);
            wrapperId = wrapper.Id;
        }

        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = wrapperId, UnitOfMeasureId = kilogramId, BaseUnitsPerUnit = 240m }, Cancellation);
        (await repository.GetForRawMaterialAsync(wrapperId, Cancellation)).Single().BaseUnitsPerUnit.Should().Be(240m);
    }

    [Fact]
    public async Task AddAsync_rejects_the_materials_own_base_unit()
    {
        var (ribbonId, centimeterId) = await SeedRibbonAsync();

        var act = () => CreateRepository().AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = centimeterId, BaseUnitsPerUnit = 1m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_unit_the_material_already_has()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m }, Cancellation);

        var act = () => repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 500m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has*");
    }

    [Fact]
    public async Task AddAsync_rejects_a_third_unit()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var meterId = await SeedUnitOfMeasureAsync("meter", "m");
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var inchId = await SeedUnitOfMeasureAsync("inch", "in");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = meterId, BaseUnitsPerUnit = 100m }, Cancellation);
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m }, Cancellation);

        var act = () => repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = inchId, BaseUnitsPerUnit = 2.54m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*at most 2*");
        (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Should().HaveCount(2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task AddAsync_rejects_a_multiplier_that_is_not_positive(decimal baseUnitsPerUnit)
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");

        var act = () => CreateRepository().AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollId, BaseUnitsPerUnit = baseUnitsPerUnit }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_nonexistent_material()
    {
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");

        var act = () => CreateRepository().AddAsync(new RawMaterialUnit { RawMaterialId = 999_999, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Correcting_the_multiplier_of_the_latest_prices_unit_reprices_the_material()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var rollUnitOfMeasureId = await SeedUnitOfMeasureAsync("roll", "roll");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollUnitOfMeasureId, BaseUnitsPerUnit = 1000m }, Cancellation);
        var roll = (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Single();
        await SetPriceAsync(ribbonId, roll.Id, 45m);
        (await GetCostPerBaseUnitAsync(ribbonId)).Should().Be(0.045m);

        // The roll is really 900 cm.
        roll.BaseUnitsPerUnit = 900m;
        await repository.UpdateAsync(roll, Cancellation);

        (await GetCostPerBaseUnitAsync(ribbonId)).Should().Be(0.05m); // 45 / 900 cm
    }

    [Fact]
    public async Task UpdateAsync_rejects_turning_a_unit_into_the_base_unit()
    {
        var (ribbonId, centimeterId) = await SeedRibbonAsync();
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m }, Cancellation);
        var roll = (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Single();

        roll.UnitOfMeasureId = centimeterId;
        var act = () => repository.UpdateAsync(roll, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_an_unused_unit()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m }, Cancellation);
        var roll = (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Single();

        await repository.DeleteAsync(roll.Id, Cancellation);

        (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_unit_a_price_is_recorded_in()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var rollUnitOfMeasureId = await SeedUnitOfMeasureAsync("roll", "roll");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = rollUnitOfMeasureId, BaseUnitsPerUnit = 1000m }, Cancellation);
        var roll = (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Single();
        await SetPriceAsync(ribbonId, roll.Id, 45m);

        var act = () => repository.DeleteAsync(roll.Id, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'roll' is used by 1 purchase price*");
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_unit_a_packaging_line_is_entered_in()
    {
        var (ribbonId, _) = await SeedRibbonAsync();
        var meterUnitOfMeasureId = await SeedUnitOfMeasureAsync("meter", "m");
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialUnit { RawMaterialId = ribbonId, UnitOfMeasureId = meterUnitOfMeasureId, BaseUnitsPerUnit = 100m }, Cancellation);
        var meter = (await repository.GetForRawMaterialAsync(ribbonId, Cancellation)).Single();
        int productId;
        await using (var dbContext = CreateDbContext())
        {
            var product = new Product { Name = "Gift Box Set" };
            dbContext.Products.Add(product);
            await dbContext.SaveChangesAsync(Cancellation);
            productId = product.Id;
        }
        await new PackagingLineRepository(CreateDbContextFactory()).SetLinesAsync(
            productId, [(ribbonId, meter.Id, PackagingLineAuthoredField.QtyPerUnit, 0.5m)], Cancellation);

        var act = () => repository.DeleteAsync(meter.Id, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("'meter' is used by 1 packaging line*");
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_nonexistent_unit()
    {
        var act = () => CreateRepository().DeleteAsync(999_999, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
