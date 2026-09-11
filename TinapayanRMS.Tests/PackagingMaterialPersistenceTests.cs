using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class PackagingMaterialPersistenceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(fixture.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private TinapayanRMSDbContext CreateDbContext() =>
        new(CreateDbContextOptions());

    private IDbContextFactory<TinapayanRMSDbContext> CreateDbContextFactory() =>
        new TestDbContextFactory(CreateDbContextOptions());

    private DbContextOptions<TinapayanRMSDbContext> CreateDbContextOptions() =>
        new DbContextOptionsBuilder<TinapayanRMSDbContext>().UseSqlServer(fixture.ConnectionString).Options;

    private sealed class TestDbContextFactory(DbContextOptions<TinapayanRMSDbContext> options) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => new(options);
    }

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
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Satin Ribbon",
            PackagingCategoryId = categoryId,
            StandardUnitId = WellKnownUnitOfMeasureIds.Centimeter,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 1000m,
        }, Cancellation);

        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        packagingMaterial.Name.Should().Be("Satin Ribbon");
        packagingMaterial.StandardUnitId.Should().Be(WellKnownUnitOfMeasureIds.Centimeter);
        packagingMaterial.PackagingCategory.Should().NotBeNull();
        packagingMaterial.StandardUnit.Should().NotBeNull();
        packagingMaterial.PurchaseUnit.Should().NotBeNull();
    }

    [Fact]
    public async Task A_packaging_material_with_piece_as_standard_unit_can_be_added()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Pastry Box",
            PackagingCategoryId = categoryId,
            StandardUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 100m,
        }, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task AddAsync_rejects_gram_as_a_standard_unit()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        var act = () => repository.AddAsync(new PackagingMaterial
        {
            Name = "Invalid",
            PackagingCategoryId = categoryId,
            StandardUnitId = WellKnownUnitOfMeasureIds.Gram,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 1m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_packaging_material()
    {
        var categoryId = await SeedCategoryAsync();
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Twine",
            PackagingCategoryId = categoryId,
            StandardUnitId = WellKnownUnitOfMeasureIds.Meter,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
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
        var repository = new PackagingMaterialRepository(CreateDbContextFactory());

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Label",
            PackagingCategoryId = categoryId,
            StandardUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 500m,
        }, Cancellation);

        var act = () => repository.AddAsync(new PackagingMaterial
        {
            Name = "Label",
            PackagingCategoryId = categoryId,
            StandardUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitId = WellKnownUnitOfMeasureIds.Piece,
            PurchaseUnitQty = 500m,
        }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
