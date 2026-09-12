using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class PackagingMaterialPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new PackagingCategory { Name = "Ribbons" };
        dbContext.PackagingCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    [Fact]
    public async Task A_packaging_material_with_a_length_standard_unit_can_be_added_and_read_back()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm", isLengthUnit: true, millimetersPerUnit: 10m);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Satin Ribbon",
            PackagingCategoryId = categoryId,
            StandardUnitId = centimeterId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 1000m,
        }, Cancellation);

        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        packagingMaterial.Name.Should().Be("Satin Ribbon");
        packagingMaterial.StandardUnitId.Should().Be(centimeterId);
        packagingMaterial.PackagingCategory.Should().NotBeNull();
        packagingMaterial.StandardUnit.Should().NotBeNull();
        packagingMaterial.PurchaseUnit.Should().NotBeNull();
    }

    [Fact]
    public async Task A_packaging_material_with_piece_as_standard_unit_can_be_added()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Pastry Box",
            PackagingCategoryId = categoryId,
            StandardUnitId = pieceId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 100m,
        }, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task GetByIdAsync_returns_the_packaging_material_with_related_data_populated()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm", isLengthUnit: true, millimetersPerUnit: 10m);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());
        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Satin Ribbon",
            PackagingCategoryId = categoryId,
            StandardUnitId = centimeterId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 1000m,
        }, Cancellation);
        var seeded = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;

        var byId = await repository.GetByIdAsync(seeded.Id, Cancellation);

        byId.Should().NotBeNull();
        byId!.Name.Should().Be("Satin Ribbon");
        byId.PackagingCategory.Should().NotBeNull();
        byId.StandardUnit.Should().NotBeNull();
        byId.PurchaseUnit.Should().NotBeNull();
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_a_nonexistent_packaging_material()
    {
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        (await repository.GetByIdAsync(999_999, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_renames_a_packaging_material_and_preserves_CurrentCost()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm", isLengthUnit: true, millimetersPerUnit: 10m);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());
        var priceHistoryRepository = new PackagingPriceHistoryRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Satin Ribbon",
            PackagingCategoryId = categoryId,
            StandardUnitId = centimeterId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 1000m,
        }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        await priceHistoryRepository.AddAsync(new PackagingPriceHistory { PackagingMaterialId = packagingMaterial.Id, Date = new DateOnly(2026, 1, 1), Price = 45m }, Cancellation);

        // Rename and correct a mistyped purchase-unit quantity -- no new price entry.
        packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        packagingMaterial.Name = "Grosgrain Ribbon";
        packagingMaterial.PurchaseUnitQty = 900m;
        await repository.UpdateAsync(packagingMaterial, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Grosgrain Ribbon");
        updated.PurchaseUnitQty.Should().Be(900m);
        // A blind dbContext.Set<T>().Update(incoming) regression would silently zero this
        // server-owned field on every plain rename (PLAN.md's documented Phase 6 bug,
        // fixed in both IngredientRepository and PackagingMaterialRepository at once).
        updated.CurrentCost.Should().Be(45m);
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_packaging_material()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        var act = () => repository.UpdateAsync(new PackagingMaterial
        {
            Id = 999_999,
            Name = "Ghost",
            PackagingCategoryId = categoryId,
            StandardUnitId = pieceId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 1m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_packaging_material()
    {
        var categoryId = await SeedCategoryAsync();
        var meterId = await SeedUnitOfMeasureAsync("meter", "m", isLengthUnit: true, millimetersPerUnit: 1000m);
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Twine",
            PackagingCategoryId = categoryId,
            StandardUnitId = meterId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 50m,
        }, Cancellation);

        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        await repository.DeleteAsync(packagingMaterial.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_packaging_material_names_are_rejected()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Label",
            PackagingCategoryId = categoryId,
            StandardUnitId = pieceId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 500m,
        }, Cancellation);

        var act = () => repository.AddAsync(new PackagingMaterial
        {
            Name = "Label",
            PackagingCategoryId = categoryId,
            StandardUnitId = pieceId,
            PurchaseUnitId = pieceId,
            PurchaseUnitQty = 500m,
        }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
