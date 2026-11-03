using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Names go in without spaces at either end, however they're written. PostgreSQL's collation counts a space at the end, so
/// "Pandesal " beside "Pandesal" got past the unique index (found 2026-11-03), where SQL Server's had ignored it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class StoredNamePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository Products() => new(CreateDbContextFactory());

    private RawMaterialRepository RawMaterials() => new(CreateDbContextFactory());

    private UnitOfMeasureRepository Units() => new(CreateDbContextFactory());

    [Fact]
    public async Task A_products_name_is_stored_without_spaces_however_its_written()
    {
        var products = Products();

        var id = await products.SaveAsync(new Product { Name = "  Pandesal " }, null, null, null, null, Cancellation);
        (await products.GetByIdAsync(id, Cancellation))!.Name.Should().Be("Pandesal", "a new product");

        await products.SaveAsync(new Product { Id = id, Name = "Pandesal Classic " }, null, null, null, null, Cancellation);
        (await products.GetByIdAsync(id, Cancellation))!.Name.Should().Be("Pandesal Classic", "a saved one");

        await products.RenameAsync(id, " Pan de Sal ", Cancellation);
        (await products.GetByIdAsync(id, Cancellation))!.Name.Should().Be("Pan de Sal", "a rename");
    }

    [Fact]
    public async Task A_product_named_as_another_but_for_a_space_at_the_end_is_a_duplicate()
    {
        var products = Products();
        await products.SaveAsync(new Product { Name = "Pandesal" }, null, null, null, null, Cancellation);

        var again = () => products.SaveAsync(new Product { Name = "Pandesal " }, null, null, null, null, Cancellation);

        await again.Should().ThrowAsync<DbUpdateException>();
        (await products.GetAllAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_product_named_with_nothing_but_spaces_is_refused()
    {
        var products = Products();

        var add = () => products.SaveAsync(new Product { Name = "   " }, null, null, null, null, Cancellation);

        await add.Should().ThrowAsync<BusinessRuleException>().WithMessage("Every product needs a name.");
        (await products.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_materials_name_is_stored_without_spaces_however_its_written()
    {
        var categoryId = await SeedCategoryAsync();
        var materials = RawMaterials();

        var id = await materials.SaveAsync(NewIngredient("  Flour ", categoryId), [], null, null, Cancellation);
        (await materials.GetByIdAsync(id, Cancellation))!.Name.Should().Be("Flour", "a new material");

        var saved = (await materials.GetByIdAsync(id, Cancellation))!;
        saved.Name = "Bread Flour ";
        await materials.UpdateAsync(saved, Cancellation);
        (await materials.GetByIdAsync(id, Cancellation))!.Name.Should().Be("Bread Flour", "a saved one");

        await materials.RenameAsync(id, " Cake Flour ", Cancellation);
        (await materials.GetByIdAsync(id, Cancellation))!.Name.Should().Be("Cake Flour", "a rename");
    }

    [Fact]
    public async Task A_material_named_as_another_but_for_a_space_at_the_end_is_a_duplicate()
    {
        var categoryId = await SeedCategoryAsync();
        var materials = RawMaterials();
        await materials.SaveAsync(NewIngredient("Flour", categoryId), [], null, null, Cancellation);

        var again = () => materials.SaveAsync(NewIngredient("Flour ", categoryId), [], null, null, Cancellation);

        await again.Should().ThrowAsync<DbUpdateException>();
        (await materials.GetAllAsync(Cancellation)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_material_named_with_nothing_but_spaces_is_refused()
    {
        var categoryId = await SeedCategoryAsync();
        var materials = RawMaterials();

        var add = () => materials.SaveAsync(NewIngredient("   ", categoryId), [], null, null, Cancellation);

        await add.Should().ThrowAsync<BusinessRuleException>().WithMessage("Every raw material needs a name.");
        (await materials.GetAllAsync(Cancellation)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_units_name_and_abbreviation_are_stored_without_spaces_however_theyre_written()
    {
        var units = Units();

        await units.AddAsync(new UnitOfMeasure { Name = " Bag ", Abbreviation = " bg " }, Cancellation);
        var added = (await units.GetAllAsync(Cancellation)).Single();
        (added.Name, added.Abbreviation).Should().Be(("bag", "bg"), "a new unit");

        await units.UpdateAsync(new UnitOfMeasure { Id = added.Id, Name = "Sack ", Abbreviation = "sk " }, Cancellation);
        var updated = (await units.GetAllAsync(Cancellation)).Single();
        (updated.Name, updated.Abbreviation).Should().Be(("sack", "sk"), "a saved one");
    }

    [Theory]
    [InlineData("bag ", "other")]
    [InlineData("other", "bg ")]
    public async Task A_unit_named_as_another_but_for_a_space_at_the_end_is_a_duplicate(string name, string abbreviation)
    {
        var units = Units();
        await units.AddAsync(new UnitOfMeasure { Name = "bag", Abbreviation = "bg" }, Cancellation);

        var again = () => units.AddAsync(new UnitOfMeasure { Name = name, Abbreviation = abbreviation }, Cancellation);

        await again.Should().ThrowAsync<DbUpdateException>();
    }

    private static RawMaterial NewIngredient(string name, int categoryId) =>
        new() { Name = name, Type = RawMaterialType.Ingredient, Source = RawMaterialSource.Purchased, CategoryId = categoryId };

    private async Task<int> SeedCategoryAsync()
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = "Flours" };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }
}
