using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

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
    public async Task A_category_is_for_ingredients_unless_saved_for_packaging_and_its_type_round_trips()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new RawMaterialCategory { Name = "Flour" }, Cancellation);
        await repository.AddAsync(new RawMaterialCategory { Name = "Boxes", Type = RawMaterialType.Packaging }, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Select(c => (c.Name, c.Type))
            .Should().Equal(("Boxes", RawMaterialType.Packaging), ("Flour", RawMaterialType.Ingredient));

        // Nothing is in it yet, so its type can still change.
        var flour = (await repository.GetAllAsync(Cancellation)).Single(c => c.Name == "Flour");
        await repository.UpdateAsync(new RawMaterialCategory { Id = flour.Id, Name = "Labels", Type = RawMaterialType.Packaging }, Cancellation);
        (await repository.GetAllAsync(Cancellation)).Single(c => c.Id == flour.Id).Type.Should().Be(RawMaterialType.Packaging);
    }

    [Fact]
    public async Task A_material_goes_only_in_a_category_of_its_own_type()
    {
        var flour = await SeedCategoryAsync("Flour");
        var boxesId = await AddPackagingCategoryAsync("Boxes");
        var pieceId = await SeedUnitOfMeasureAsync("piece", "pc");
        var materials = new RawMaterialRepository(CreateDbContextFactory(), CreateCostPropagator());

        var boxAsFlour = () => materials.AddAsync(
            new RawMaterial { Name = "Cake Box", Type = RawMaterialType.Packaging, CategoryId = flour.Id, BaseUnitId = pieceId }, Cancellation);
        var flourAsBox = () => materials.AddAsync(new RawMaterial { Name = "Bread Flour", CategoryId = boxesId }, Cancellation);

        await boxAsFlour.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Flour is an ingredient category. Put this packaging material in a category for packaging.");
        await flourAsBox.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Boxes is a packaging category. Put this ingredient in a category for ingredients.");

        await materials.AddAsync(new RawMaterial { Name = "Cake Box", Type = RawMaterialType.Packaging, CategoryId = boxesId, BaseUnitId = pieceId }, Cancellation);
        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.SingleAsync(Cancellation)).CategoryId.Should().Be(boxesId);
    }

    [Fact]
    public async Task A_categorys_type_cant_change_while_a_material_is_in_it()
    {
        var flour = await SeedCategoryAsync("Flour");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterials.Add(new RawMaterial { Name = "Bread Flour", CategoryId = flour.Id });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var retype = () => CreateRepository().UpdateAsync(new RawMaterialCategory { Id = flour.Id, Name = "Flour", Type = RawMaterialType.Packaging }, Cancellation);

        await retype.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Flour has 1 material in it, so its type can't change. Move it to another category first.");

        // A rename alone is still fine.
        await CreateRepository().UpdateAsync(new RawMaterialCategory { Id = flour.Id, Name = "Flours" }, Cancellation);
        (await CreateRepository().GetAllAsync(Cancellation)).Single().Should().BeEquivalentTo(new { Name = "Flours", Type = RawMaterialType.Ingredient });
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
