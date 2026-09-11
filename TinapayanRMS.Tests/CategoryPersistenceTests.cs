using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Tests.Infrastructure;

namespace TinapayanRMS.Tests;

[Collection(DatabaseCollection.Name)]
public class CategoryPersistenceTests(DatabaseFixture fixture) : IAsyncLifetime
{
    private CancellationToken Cancellation => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => new(fixture.ResetAsync());

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private TinapayanRMSDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TinapayanRMSDbContext>().UseSqlServer(fixture.ConnectionString).Options);

    [Fact]
    public async Task The_four_protected_length_units_and_gram_and_piece_are_seeded()
    {
        await using var dbContext = CreateDbContext();

        var units = await dbContext.UnitsOfMeasure.OrderBy(u => u.Id).ToListAsync(Cancellation);

        units.Should().HaveCount(6);
        units.Select(u => (u.Id, u.Name, u.Abbreviation)).Should().BeEquivalentTo(
        [
            (WellKnownUnitOfMeasureIds.Millimeter, "Millimeter", "mm"),
            (WellKnownUnitOfMeasureIds.Centimeter, "Centimeter", "cm"),
            (WellKnownUnitOfMeasureIds.Inch, "Inch", "in"),
            (WellKnownUnitOfMeasureIds.Meter, "Meter", "m"),
            (5, "Gram", "g"),
            (6, "Piece", "pc"),
        ]);
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
}
