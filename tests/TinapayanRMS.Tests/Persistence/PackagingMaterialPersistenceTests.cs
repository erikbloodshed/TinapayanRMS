using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class PackagingMaterialPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private PackagingMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new PackagingCategory { Name = "Ribbons" };
        dbContext.PackagingCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    [Fact]
    public async Task A_packaging_material_can_be_added_and_read_back_with_its_base_unit()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var repository = CreateRepository();

        await repository.AddAsync(new PackagingMaterial
        {
            Name = "Satin Ribbon",
            PackagingCategoryId = categoryId,
            BaseUnitId = centimeterId,
        }, Cancellation);

        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        packagingMaterial.Name.Should().Be("Satin Ribbon");
        packagingMaterial.BaseUnitId.Should().Be(centimeterId);
        packagingMaterial.PackagingCategory.Should().NotBeNull();
        packagingMaterial.BaseUnit!.Abbreviation.Should().Be("cm");
        packagingMaterial.Units.Should().BeEmpty();
        packagingMaterial.CostPerBaseUnit.Should().Be(0m);
    }

    [Fact]
    public async Task GetAllAsync_and_GetByIdAsync_include_the_units_largest_first_with_their_unit_of_measure()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var meterId = await SeedUnitOfMeasureAsync("meter", "m");
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        await using (var dbContext = CreateDbContext())
        {
            dbContext.PackagingMaterials.Add(new PackagingMaterial
            {
                Name = "Satin Ribbon",
                PackagingCategoryId = categoryId,
                BaseUnitId = centimeterId,
                Units =
                [
                    new PackagingMaterialUnit { UnitOfMeasureId = meterId, BaseUnitsPerUnit = 100m },
                    new PackagingMaterialUnit { UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m, Description = "10 m roll" },
                ],
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var repository = CreateRepository();
        var fromAll = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var byId = await repository.GetByIdAsync(fromAll.Id, Cancellation);

        foreach (var material in new[] { fromAll, byId! })
        {
            material.BaseUnit.Should().NotBeNull();
            material.Units.Select(u => u.UnitOfMeasure!.Name).Should().Equal("roll", "meter");
            material.Units[0].Description.Should().Be("10 m roll");
        }
    }

    [Fact]
    public async Task GetByIdAsync_returns_null_for_a_nonexistent_packaging_material()
    {
        (await CreateRepository().GetByIdAsync(999_999, Cancellation)).Should().BeNull();
    }

    [Fact]
    public async Task UpdateAsync_renames_a_packaging_material_and_preserves_its_cost()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var repository = CreateRepository();

        await repository.AddAsync(new PackagingMaterial { Name = "Satin Ribbon", PackagingCategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        await new PackagingPriceHistoryRepository(CreateDbContextFactory()).AddAsync(
            new PackagingPriceHistory { PackagingMaterialId = packagingMaterial.Id, Date = new DateOnly(2026, 1, 1), Price = 0.045m },
            Cancellation);

        packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        packagingMaterial.Name = "Grosgrain Ribbon";
        packagingMaterial.CostPerBaseUnit = 999m; // the form never owns this
        await repository.UpdateAsync(packagingMaterial, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Grosgrain Ribbon");
        // A blind dbContext.Set<T>().Update(incoming) regression would overwrite this server-owned
        // field on every plain rename (PLAN.md's documented Phase 6 bug).
        updated.CostPerBaseUnit.Should().Be(0.045m);
    }

    [Fact]
    public async Task UpdateAsync_can_change_the_base_unit_while_nothing_is_measured_in_it()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var inchId = await SeedUnitOfMeasureAsync("inch", "in");
        var repository = CreateRepository();
        await repository.AddAsync(new PackagingMaterial { Name = "Satin Ribbon", PackagingCategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Single();

        packagingMaterial.BaseUnitId = inchId;
        await repository.UpdateAsync(packagingMaterial, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Single().BaseUnit!.Abbreviation.Should().Be("in");
    }

    [Fact]
    public async Task UpdateAsync_rejects_changing_the_base_unit_once_the_material_has_an_alternative_unit()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var inchId = await SeedUnitOfMeasureAsync("inch", "in");
        var rollId = await SeedUnitOfMeasureAsync("roll", "roll");
        var repository = CreateRepository();
        await repository.AddAsync(new PackagingMaterial { Name = "Satin Ribbon", PackagingCategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Single();
        await new PackagingMaterialUnitRepository(CreateDbContextFactory()).AddAsync(
            new PackagingMaterialUnit { PackagingMaterialId = packagingMaterial.Id, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m }, Cancellation);

        packagingMaterial.BaseUnitId = inchId;
        var act = () => repository.UpdateAsync(packagingMaterial, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_changing_the_base_unit_once_the_material_is_priced()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var inchId = await SeedUnitOfMeasureAsync("inch", "in");
        var repository = CreateRepository();
        await repository.AddAsync(new PackagingMaterial { Name = "Satin Ribbon", PackagingCategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Single();
        await new PackagingPriceHistoryRepository(CreateDbContextFactory()).AddAsync(
            new PackagingPriceHistory { PackagingMaterialId = packagingMaterial.Id, Date = new DateOnly(2026, 1, 1), Price = 0.045m }, Cancellation);

        packagingMaterial.BaseUnitId = inchId;
        var act = () => repository.UpdateAsync(packagingMaterial, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_packaging_material()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        var act = () => CreateRepository().UpdateAsync(new PackagingMaterial
        {
            Id = 999_999,
            Name = "Ghost",
            PackagingCategoryId = categoryId,
            BaseUnitId = pieceId,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_the_packaging_material_with_its_units_and_prices()
    {
        var categoryId = await SeedCategoryAsync();
        var meterId = await SeedUnitOfMeasureAsync("meter", "m");
        var spoolId = await SeedUnitOfMeasureAsync("spool", "spool");
        var repository = CreateRepository();

        await repository.AddAsync(new PackagingMaterial { Name = "Twine", PackagingCategoryId = categoryId, BaseUnitId = meterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var unitRepository = new PackagingMaterialUnitRepository(CreateDbContextFactory());
        await unitRepository.AddAsync(new PackagingMaterialUnit { PackagingMaterialId = packagingMaterial.Id, UnitOfMeasureId = spoolId, BaseUnitsPerUnit = 50m }, Cancellation);
        var spool = (await unitRepository.GetForPackagingMaterialAsync(packagingMaterial.Id, Cancellation)).Single();
        await new PackagingPriceHistoryRepository(CreateDbContextFactory()).AddAsync(
            new PackagingPriceHistory { PackagingMaterialId = packagingMaterial.Id, PackagingMaterialUnitId = spool.Id, Date = new DateOnly(2026, 1, 1), Price = 100m }, Cancellation);

        await repository.DeleteAsync(packagingMaterial.Id, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.PackagingMaterials.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.PackagingMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.PackagingPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task Duplicate_packaging_material_names_are_rejected()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();

        await repository.AddAsync(new PackagingMaterial { Name = "Label", PackagingCategoryId = categoryId, BaseUnitId = pieceId }, Cancellation);

        var act = () => repository.AddAsync(new PackagingMaterial { Name = "Label", PackagingCategoryId = categoryId, BaseUnitId = pieceId }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
