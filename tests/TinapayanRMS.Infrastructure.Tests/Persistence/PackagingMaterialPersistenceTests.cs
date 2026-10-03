using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

[Collection(DatabaseCollection.Name)]
public class PackagingMaterialPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private RawMaterialRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Ribbons", Type = RawMaterialType.Packaging };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    [Fact]
    public async Task A_packaging_material_can_be_added_and_read_back_with_its_base_unit()
    {
        var categoryId = await SeedCategoryAsync();
        var centimeterId = await SeedUnitOfMeasureAsync("centimeter", "cm");
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Satin Ribbon",
            CategoryId = categoryId,
            BaseUnitId = centimeterId,
        }, Cancellation);

        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        packagingMaterial.Name.Should().Be("Satin Ribbon");
        packagingMaterial.BaseUnitId.Should().Be(centimeterId);
        packagingMaterial.Category.Should().NotBeNull();
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
            dbContext.RawMaterials.Add(new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = "Satin Ribbon",
                CategoryId = categoryId,
                BaseUnitId = centimeterId,
                Units =
                [
                    new RawMaterialUnit { UnitOfMeasureId = meterId, BaseUnitsPerUnit = 100m },
                    new RawMaterialUnit { UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m, Description = "10 m roll" },
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

        await repository.AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Satin Ribbon", CategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        await SetPriceAsync(packagingMaterial.Id, null, 0.045m);

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
        await repository.AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Satin Ribbon", CategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
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
        await repository.AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Satin Ribbon", CategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Single();
        await new RawMaterialUnitRepository(CreateDbContextFactory()).AddAsync(
            new RawMaterialUnit { RawMaterialId = packagingMaterial.Id, UnitOfMeasureId = rollId, BaseUnitsPerUnit = 1000m }, Cancellation);

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
        await repository.AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Satin Ribbon", CategoryId = categoryId, BaseUnitId = centimeterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Single();
        await SetPriceAsync(packagingMaterial.Id, null, 0.045m);

        packagingMaterial.BaseUnitId = inchId;
        var act = () => repository.UpdateAsync(packagingMaterial, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_packaging_material()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        var act = () => CreateRepository().UpdateAsync(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Id = 999_999,
            Name = "Ghost",
            CategoryId = categoryId,
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

        await repository.AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Twine", CategoryId = categoryId, BaseUnitId = meterId }, Cancellation);
        var packagingMaterial = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        var unitRepository = new RawMaterialUnitRepository(CreateDbContextFactory());
        await unitRepository.AddAsync(new RawMaterialUnit { RawMaterialId = packagingMaterial.Id, UnitOfMeasureId = spoolId, BaseUnitsPerUnit = 50m }, Cancellation);
        var spool = (await unitRepository.GetForRawMaterialAsync(packagingMaterial.Id, Cancellation)).Single();
        await SetPriceAsync(packagingMaterial.Id, spool.Id, 100m);

        await repository.DeleteAsync(packagingMaterial.Id, Cancellation);

        await using var dbContext = CreateDbContext();
        (await dbContext.RawMaterials.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialUnits.AnyAsync(Cancellation)).Should().BeFalse();
        (await dbContext.RawMaterialPriceHistories.AnyAsync(Cancellation)).Should().BeFalse();
    }

    [Fact]
    public async Task AddAsync_rejects_a_packaging_material_without_a_base_unit()
    {
        var categoryId = await SeedCategoryAsync();

        var act = () => CreateRepository().AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Label", CategoryId = categoryId }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*base unit*");
    }

    [Fact]
    public async Task AddAsync_rejects_a_made_packaging_material()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");

        var act = () => CreateRepository().AddAsync(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Label",
            CategoryId = categoryId,
            BaseUnitId = pieceId,
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
        }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*packaging is always purchased*");
    }

    [Fact]
    public async Task The_database_rejects_a_made_packaging_material_even_past_the_repository()
    {
        // A check constraint backs RawMaterialRules.EnsureFieldsMatchType up.
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        await using var dbContext = CreateDbContext();
        dbContext.RawMaterials.Add(new RawMaterial
        {
            Type = RawMaterialType.Packaging,
            Name = "Label",
            CategoryId = categoryId,
            BaseUnitId = pieceId,
            Source = RawMaterialSource.Made,
            WeightLossPercent = 0m,
        });

        var act = () => dbContext.SaveChangesAsync(Cancellation);

        (await act.Should().ThrowAsync<DbUpdateException>()).WithInnerException<Microsoft.Data.SqlClient.SqlException>()
            .WithMessage("*CK_RawMaterials_OnlyIngredientsAreMade*");
    }

    [Fact]
    public async Task Duplicate_packaging_material_names_are_rejected()
    {
        var categoryId = await SeedCategoryAsync();
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        var repository = CreateRepository();

        await repository.AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Label", CategoryId = categoryId, BaseUnitId = pieceId }, Cancellation);

        var act = () => repository.AddAsync(new RawMaterial { Type = RawMaterialType.Packaging, Name = "Label", CategoryId = categoryId, BaseUnitId = pieceId }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
