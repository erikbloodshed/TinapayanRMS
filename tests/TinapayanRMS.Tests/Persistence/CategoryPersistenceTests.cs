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
    public async Task A_category_can_be_inserted_and_read_back()
    {
        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = "Flour" });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.RawMaterialCategories.SingleAsync(Cancellation);
            category.Name.Should().Be("Flour");
        }
    }

    [Fact]
    public async Task Duplicate_category_names_are_rejected()
    {
        await using var dbContext = CreateDbContext();

        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = "Dairy" });
        await dbContext.SaveChangesAsync(Cancellation);

        dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = "Dairy" });
        var act = () => dbContext.SaveChangesAsync(Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task One_category_can_hold_both_ingredients_and_packaging()
    {
        var category = await SeedCategoryAsync("Butter");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");

        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterials.Add(new RawMaterial { Name = "Unsalted Butter", Type = RawMaterialType.Ingredient, CategoryId = category.Id });
            dbContext.RawMaterials.Add(new RawMaterial { Name = "Butter Tub", Type = RawMaterialType.Packaging, CategoryId = category.Id, BaseUnitId = pieceId });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        await using (var dbContext = CreateDbContext())
        {
            var types = await dbContext.RawMaterials.Where(m => m.CategoryId == category.Id).Select(m => m.Type).ToListAsync(Cancellation);
            types.Should().BeEquivalentTo([RawMaterialType.Ingredient, RawMaterialType.Packaging]);
        }
    }

    private RawMaterialCategoryRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<RawMaterialCategory> SeedCategoryAsync(string name)
    {
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialCategory { Name = name }, Cancellation);
        return (await repository.GetAllAsync(Cancellation)).Single(c => c.Name == name);
    }

    [Fact]
    public async Task RawMaterialCategoryRepository_AddAsync_GetAllAsync_and_UpdateAsync_round_trip_a_category()
    {
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterialCategory { Name = "Flour" }, Cancellation);

        var added = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        added.Name.Should().Be("Flour");

        await repository.UpdateAsync(new RawMaterialCategory { Id = added.Id, Name = "Flours & Starches" }, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Flours & Starches");
    }

    [Fact]
    public async Task RawMaterialCategoryRepository_GetAllAsync_orders_by_name()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialCategory { Name = "Ribbons" }, Cancellation);
        await repository.AddAsync(new RawMaterialCategory { Name = "Boxes" }, Cancellation);
        await repository.AddAsync(new RawMaterialCategory { Name = "Flour" }, Cancellation);

        var names = (await repository.GetAllAsync(Cancellation)).Select(c => c.Name);

        names.Should().Equal("Boxes", "Flour", "Ribbons");
    }

    [Fact]
    public async Task RawMaterialCategoryRepository_UpdateAsync_rejects_a_nonexistent_category()
    {
        var act = () => CreateRepository().UpdateAsync(new RawMaterialCategory { Id = 999_999, Name = "Ghost" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task RawMaterialCategoryRepository_DeleteAsync_removes_an_unused_category()
    {
        var repository = CreateRepository();
        var category = await SeedCategoryAsync("Flour");

        await repository.DeleteAsync(category.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task Deleting_a_category_that_an_ingredient_is_in_is_blocked()
    {
        var category = await SeedCategoryAsync("Flour");

        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterials.Add(new RawMaterial
            {
                Name = "All-Purpose Flour",
                Type = RawMaterialType.Ingredient,
                CategoryId = category.Id,
                Source = RawMaterialSource.Purchased,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var act = () => CreateRepository().DeleteAsync(category.Id, Cancellation);

        // ExecuteDeleteAsync issues a raw DELETE outside EF Core's change tracker, so an
        // FK violation surfaces as the driver's own SqlException, not a DbUpdateException.
        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Deleting_a_category_that_a_packaging_material_is_in_is_blocked()
    {
        var category = await SeedCategoryAsync("Ribbons");
        var baseUnitId = await SeedUnitOfMeasureAsync("centimeter", "cm");

        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterials.Add(new RawMaterial
            {
                Name = "Satin Ribbon",
                Type = RawMaterialType.Packaging,
                CategoryId = category.Id,
                BaseUnitId = baseUnitId,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var act = () => CreateRepository().DeleteAsync(category.Id, Cancellation);

        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }
}
