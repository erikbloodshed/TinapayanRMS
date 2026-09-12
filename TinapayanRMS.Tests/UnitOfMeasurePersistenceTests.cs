using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore;
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

        await repository.AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bag" }, Cancellation);

        var units = await repository.GetAllAsync(Cancellation);
        units.Should().Contain(u => u.Name == "Bag" && u.Abbreviation == "bag");
    }

    [Fact]
    public async Task UpdateAsync_renames_an_unprotected_unit()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bag" }, Cancellation);
        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "Bag").Subject;

        await repository.UpdateAsync(new UnitOfMeasure { Id = unit.Id, Name = "Sack", Abbreviation = "sk" }, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle(u => u.Id == unit.Id).Subject;
        updated.Name.Should().Be("Sack");
        updated.Abbreviation.Should().Be("sk");
    }

    [Fact]
    public void Gram_and_Piece_are_seeded_but_not_protected()
    {
        // Gram (5) and Piece (6) are seeded but, unlike the four length units, are not
        // in ProtectedLengthUnitIds -- so UpdateAsync/DeleteAsync allow them through to
        // the same code path already proven generically by the unprotected-unit tests
        // above. Asserted here against the pure rule rather than by actually renaming
        // the shared seeded row, which every other test's FK references depend on.
        UnitOfMeasureRules.IsProtected(WellKnownUnitOfMeasureIds.Gram).Should().BeFalse();
        UnitOfMeasureRules.IsProtected(WellKnownUnitOfMeasureIds.Piece).Should().BeFalse();
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_nonexistent_unit()
    {
        var act = () => CreateRepository().UpdateAsync(new UnitOfMeasure { Id = 999_999, Name = "Ghost", Abbreviation = "gh" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(WellKnownUnitOfMeasureIds.Millimeter)]
    [InlineData(WellKnownUnitOfMeasureIds.Centimeter)]
    [InlineData(WellKnownUnitOfMeasureIds.Inch)]
    [InlineData(WellKnownUnitOfMeasureIds.Meter)]
    public async Task UpdateAsync_rejects_a_protected_length_unit(int protectedUnitId)
    {
        var act = () => CreateRepository().UpdateAsync(new UnitOfMeasure { Id = protectedUnitId, Name = "Renamed", Abbreviation = "rn" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Theory]
    [InlineData(WellKnownUnitOfMeasureIds.Millimeter)]
    [InlineData(WellKnownUnitOfMeasureIds.Centimeter)]
    [InlineData(WellKnownUnitOfMeasureIds.Inch)]
    [InlineData(WellKnownUnitOfMeasureIds.Meter)]
    public async Task DeleteAsync_rejects_a_protected_length_unit(int protectedUnitId)
    {
        var act = () => CreateRepository().DeleteAsync(protectedUnitId, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();

        (await CreateRepository().GetAllAsync(Cancellation)).Should().Contain(u => u.Id == protectedUnitId);
    }

    [Fact]
    public async Task DeleteAsync_removes_an_unused_unprotected_unit()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bag" }, Cancellation);
        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "Bag").Subject;

        await repository.DeleteAsync(unit.Id, Cancellation);

        (await repository.GetAllAsync(Cancellation)).Should().NotContain(u => u.Id == unit.Id);
    }

    [Fact]
    public async Task Deleting_a_unit_that_is_in_use_is_blocked()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bag" }, Cancellation);
        var unit = (await repository.GetAllAsync(Cancellation)).Should().Contain(u => u.Name == "Bag").Subject;
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
        await repository.AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bg1" }, Cancellation);

        var act = () => repository.AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bg2" }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Duplicate_unit_abbreviations_are_rejected()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new UnitOfMeasure { Name = "Bag", Abbreviation = "bg" }, Cancellation);

        var act = () => repository.AddAsync(new UnitOfMeasure { Name = "Sack", Abbreviation = "bg" }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
