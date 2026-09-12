using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class UnitOfMeasurePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private UnitOfMeasureRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new IngredientCategory { Name = "Flour" };
        dbContext.IngredientCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    [Fact]
    public async Task AddAsync_and_GetAllAsync_round_trip_a_custom_unit()
    {
        var repository = CreateRepository();

        await repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bag" }, Cancellation);

        var units = await repository.GetAllAsync(Cancellation);
        units.Should().Contain(u => u.Name == "bag" && u.Abbreviation == "bag" && !u.IsLengthUnit && u.MillimetersPerUnit == null);
    }

    [Fact]
    public async Task AddAsync_and_GetAllAsync_round_trip_a_length_unit()
    {
        var repository = CreateRepository();

        await repository.AddAsync(new UnitOfMeasure { Name = "centimeter", Abbreviation = "cm", IsLengthUnit = true, MillimetersPerUnit = 10m }, Cancellation);

        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "centimeter").Subject;
        unit.IsLengthUnit.Should().BeTrue();
        unit.MillimetersPerUnit.Should().Be(10m);
    }

    [Fact]
    public async Task UpdateAsync_renames_a_unit()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bag" }, Cancellation);
        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "bag").Subject;

        await repository.UpdateAsync(new UnitOfMeasure { Id = unit.Id, Name = "sack", Abbreviation = "sk" }, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle(u => u.Id == unit.Id).Subject;
        updated.Name.Should().Be("sack");
        updated.Abbreviation.Should().Be("sk");
    }

    [Fact]
    public async Task AddAsync_rejects_a_name_with_an_uppercase_letter()
    {
        var act = () => CreateRepository().AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bag" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_name_with_an_uppercase_letter()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bag" }, Cancellation);
        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "bag").Subject;

        var act = () => repository.UpdateAsync(new UnitOfMeasure { Id = unit.Id, Name = "Sack", Abbreviation = "sk" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_length_unit_without_a_conversion_factor()
    {
        var act = () => CreateRepository().AddAsync(new UnitOfMeasure { Name = "centimeter", Abbreviation = "cm", IsLengthUnit = true }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task AddAsync_rejects_a_non_length_unit_with_a_stray_conversion_factor()
    {
        var act = () => CreateRepository().AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bag", MillimetersPerUnit = 10m }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_unit()
    {
        var act = () => CreateRepository().UpdateAsync(new UnitOfMeasure { Id = 999_999, Name = "ghost", Abbreviation = "gh" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task DeleteAsync_removes_an_unused_unit()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bag" }, Cancellation);
        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "bag").Subject;

        await repository.DeleteAsync(unit.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().NotContain(u => u.Id == unit.Id);
    }

    [Fact]
    public async Task Deleting_a_unit_that_is_in_use_is_blocked()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bag" }, Cancellation);
        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "bag").Subject;
        var categoryId = await SeedCategoryAsync();

        await using (var dbContext = CreateDbContext())
        {
            dbContext.Ingredients.Add(new Ingredient
            {
                Name = "Flour",
                IngredientCategoryId = categoryId,
                Source = IngredientSource.Purchased,
                PurchaseUnitId = unit.Id,
                PurchaseUnitQty = 1m,
            });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var act = () => repository.DeleteAsync(unit.Id, Cancellation);

        // ExecuteDeleteAsync issues a raw DELETE outside EF Core's change tracker, so an
        // FK violation surfaces as the driver's own SqlException, not a DbUpdateException.
        await act.Should().ThrowAsync<Microsoft.Data.SqlClient.SqlException>();
    }

    [Fact]
    public async Task Duplicate_unit_names_are_rejected()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bg1" }, Cancellation);

        var act = () => repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bg2" }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Duplicate_unit_abbreviations_are_rejected()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bg" }, Cancellation);

        var act = () => repository.AddAsync(new UnitOfMeasure { Name = "sack", Abbreviation = "bg" }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
