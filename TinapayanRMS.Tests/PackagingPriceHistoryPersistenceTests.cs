using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class PackagingPriceHistoryPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private async Task<int> SeedPackagingMaterialAsync()
    {
        var standardUnitId = await GetOrCreateUnitOfMeasureAsync("centimeter", "cm", isLengthUnit: true, millimetersPerUnit: 10m);
        var purchaseUnitId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        await using var dbContext = CreateDbContext();
        var category = new PackagingCategory { Name = "Ribbons" };
        dbContext.PackagingCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);

        var packagingMaterial = new PackagingMaterial
        {
            Name = "Satin Ribbon",
            PackagingCategoryId = category.Id,
            StandardUnitId = standardUnitId,
            PurchaseUnitId = purchaseUnitId,
            PurchaseUnitQty = 1000m,
        };
        dbContext.PackagingMaterials.Add(packagingMaterial);
        await dbContext.SaveChangesAsync(Cancellation);
        return packagingMaterial.Id;
    }

    private async Task<PackagingMaterial> GetPackagingMaterialAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.PackagingMaterials.AsNoTracking().SingleAsync(p => p.Id == id, Cancellation);
    }

    [Fact]
    public async Task Adding_the_first_price_entry_sets_CurrentCost()
    {
        var packagingMaterialId = await SeedPackagingMaterialAsync();
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingPriceHistory
        {
            PackagingMaterialId = packagingMaterialId,
            Date = new DateOnly(2026, 1, 1),
            Price = 45m,
        }, Cancellation);

        var packagingMaterial = await GetPackagingMaterialAsync(packagingMaterialId);
        packagingMaterial.CurrentCost.Should().Be(45m);
    }

    [Fact]
    public async Task Adding_a_newer_dated_entry_replaces_CurrentCost()
    {
        var packagingMaterialId = await SeedPackagingMaterialAsync();
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingPriceHistory { PackagingMaterialId = packagingMaterialId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);
        await repository.AddAsync(new PackagingPriceHistory { PackagingMaterialId = packagingMaterialId, Date = new DateOnly(2026, 2, 1), Price = 48m }, Cancellation);

        var packagingMaterial = await GetPackagingMaterialAsync(packagingMaterialId);
        packagingMaterial.CurrentCost.Should().Be(48m);
    }

    [Fact]
    public async Task Deleting_the_only_entry_zeroes_CurrentCost()
    {
        var packagingMaterialId = await SeedPackagingMaterialAsync();
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingPriceHistory { PackagingMaterialId = packagingMaterialId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);
        var entry = (await repository.GetForPackagingMaterialAsync(packagingMaterialId, Cancellation)).Should().ContainSingle().Subject;

        await repository.DeleteAsync(entry.Id, Cancellation);

        var packagingMaterial = await GetPackagingMaterialAsync(packagingMaterialId);
        packagingMaterial.CurrentCost.Should().Be(0m);
    }

    [Fact]
    public async Task AddAsync_rejects_a_non_positive_price()
    {
        var packagingMaterialId = await SeedPackagingMaterialAsync();
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        var act = () => repository.AddAsync(new PackagingPriceHistory
        {
            PackagingMaterialId = packagingMaterialId,
            Date = new DateOnly(2026, 1, 1),
            Price = -5m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_nonexistent_packaging_material()
    {
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        var act = () => repository.AddAsync(new PackagingPriceHistory
        {
            PackagingMaterialId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 45m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Updating_an_entrys_price_recomputes_CurrentCost()
    {
        var packagingMaterialId = await SeedPackagingMaterialAsync();
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingPriceHistory { PackagingMaterialId = packagingMaterialId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);
        var entry = (await repository.GetForPackagingMaterialAsync(packagingMaterialId, Cancellation)).Should().ContainSingle().Subject;

        entry.Price = 48m;
        await repository.UpdateAsync(entry, Cancellation);

        var packagingMaterial = await GetPackagingMaterialAsync(packagingMaterialId);
        packagingMaterial.CurrentCost.Should().Be(48m);
    }

    [Fact]
    public async Task UpdateAsync_rejects_an_entry_for_a_nonexistent_packaging_material()
    {
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        var act = () => repository.UpdateAsync(new PackagingPriceHistory
        {
            Id = 999_999,
            PackagingMaterialId = 999_999,
            Date = new DateOnly(2026, 1, 1),
            Price = 45m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_rejects_a_nonexistent_entry()
    {
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        var act = () => repository.DeleteAsync(999_999, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Deleting_a_packaging_material_cascades_to_its_price_history()
    {
        var packagingMaterialId = await SeedPackagingMaterialAsync();
        var repository = new PackagingPriceHistoryRepository(CreateDbContextFactory());
        await repository.AddAsync(new PackagingPriceHistory { PackagingMaterialId = packagingMaterialId, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);

        await using (var dbContext = CreateDbContext())
        {
            await dbContext.PackagingMaterials.Where(p => p.Id == packagingMaterialId).ExecuteDeleteAsync(Cancellation);
        }

        (await repository.GetForPackagingMaterialAsync(packagingMaterialId, Cancellation)).Should().BeEmpty();
    }
}
