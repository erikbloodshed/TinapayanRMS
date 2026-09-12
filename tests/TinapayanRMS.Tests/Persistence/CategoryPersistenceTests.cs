using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class CategoryPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task UnitsOfMeasure_has_no_seeded_rows_by_default()
    {
        // Units are fully user-managed (no protected/well-known ids) -- a freshly
        // migrated database starts with zero rows, and the shop owner adds every unit
        // they need through the Units of Measure screen.
        await using var dbContext = CreateDbContext();

        var units = await dbContext.UnitsOfMeasure.ToListAsync(Cancellation);

        units.Should().BeEmpty();
    }

    [Fact]
    public async Task An_ingredient_category_can_be_inserted_and_read_back()
    {
        await using (var dbContext = CreateDbContext())
        {
            dbContext.IngredientCategories.Add(new IngredientCategory { Name = "Flour" });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.IngredientCategories.SingleAsync(Cancellation);
            category.Name.Should().Be("Flour");
        }
    }

    [Fact]
    public async Task A_packaging_category_can_be_inserted_and_read_back()
    {
        await using (var dbContext = CreateDbContext())
        {
            dbContext.PackagingCategories.Add(new PackagingCategory { Name = "Ribbons" });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.PackagingCategories.SingleAsync(Cancellation);
            category.Name.Should().Be("Ribbons");
        }
    }

    [Fact]
    public async Task Duplicate_ingredient_category_names_are_rejected()
    {
        await using var dbContext = CreateDbContext();

        dbContext.IngredientCategories.Add(new IngredientCategory { Name = "Dairy" });
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.IngredientCategories.Add(new IngredientCategory { Name = "Dairy" });
        var act = () => dbContext.SaveChangesAsync(Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private IngredientCategoryRepository CreateIngredientCategoryRepository() => new(CreateDbContextFactory());

    private PackagingCategoryRepository CreatePackagingCategoryRepository() => new(CreateDbContextFactory());

    [Fact]
    public async Task IngredientCategoryRepository_AddAsync_GetAllAsync_and_UpdateAsync_round_trip_a_category()
    {
        var repository = CreateIngredientCategoryRepository();

        await repository.AddAsync(new IngredientCategory { Name = "Flour" }, Cancellation);

        var added = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        added.Name.Should().Be("Flour");

        await repository.UpdateAsync(new IngredientCategory { Id = added.Id, Name = "Flours & Starches" }, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Flours & Starches");
    }

    [Fact]
    public async Task IngredientCategoryRepository_UpdateAsync_rejects_a_nonexistent_category()
    {
        var act = () => CreateIngredientCategoryRepository().UpdateAsync(new IngredientCategory { Id = 999_999, Name = "Ghost" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task IngredientCategoryRepository_DeleteAsync_removes_an_unused_category()
    {
        var repository = CreateIngredientCategoryRepository();
        await repository.AddAsync(new IngredientCategory { Name = "Flour" }, Cancellation);
        var category = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;

        await repository.DeleteAsync(category.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_an_ingredient_category_that_is_in_use_is_blocked()
    {
        var repository = CreateIngredientCategoryRepository();
        await repository.AddAsync(new IngredientCategory { Name = "Flour" }, Cancellation);
        var category = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var unitId = await SeedUnitOfMeasureAsync("piece", "pc");

        await using (var dbContext = CreateDbContext())
        {
            dbContext.Ingredients.Add(new Ingredient
            {
                Name = "All-Purpose Flour",
                IngredientCategoryId = category.Id,
                Source = IngredientSource.Purchased,
                PurchaseUnitId = unitId,
                PurchaseUnitQty = 25000m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var act = () => repository.DeleteAsync(category.Id, Cancellation);

        // ExecuteDeleteAsync issues a raw DELETE outside EF Core's change tracker, so an
        // FK violation surfaces as the driver's own SqlException, not a DbUpdateException.
        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task PackagingCategoryRepository_AddAsync_GetAllAsync_and_UpdateAsync_round_trip_a_category()
    {
        var repository = CreatePackagingCategoryRepository();

        await repository.AddAsync(new PackagingCategory { Name = "Ribbons" }, Cancellation);

        var added = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        added.Name.Should().Be("Ribbons");

        await repository.UpdateAsync(new PackagingCategory { Id = added.Id, Name = "Ribbons & Bows" }, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Ribbons & Bows");
    }

    [Fact]
    public async Task PackagingCategoryRepository_UpdateAsync_rejects_a_nonexistent_category()
    {
        var act = () => CreatePackagingCategoryRepository().UpdateAsync(new PackagingCategory { Id = 999_999, Name = "Ghost" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task PackagingCategoryRepository_DeleteAsync_removes_an_unused_category()
    {
        var repository = CreatePackagingCategoryRepository();
        await repository.AddAsync(new PackagingCategory { Name = "Ribbons" }, Cancellation);
        var category = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;

        await repository.DeleteAsync(category.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_packaging_category_that_is_in_use_is_blocked()
    {
        var repository = CreatePackagingCategoryRepository();
        await repository.AddAsync(new PackagingCategory { Name = "Ribbons" }, Cancellation);
        var category = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var standardUnitId = await SeedUnitOfMeasureAsync("centimeter", "cm", isLengthUnit: true, millimetersPerUnit: 10m);
        var purchaseUnitId = await SeedUnitOfMeasureAsync("piece", "pc");

        await using (var dbContext = CreateDbContext())
        {
            dbContext.PackagingMaterials.Add(new PackagingMaterial
            {
                Name = "Satin Ribbon",
                PackagingCategoryId = category.Id,
                StandardUnitId = standardUnitId,
                PurchaseUnitId = purchaseUnitId,
                PurchaseUnitQty = 1000m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var act = () => repository.DeleteAsync(category.Id, Cancellation);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }
}
