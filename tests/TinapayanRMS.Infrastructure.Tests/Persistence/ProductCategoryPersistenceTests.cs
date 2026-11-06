using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// Product categories (Breads, Cakes, …): a list of their own, apart from raw material categories, and optional on a
/// product, so deleting one leaves its products uncategorized.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ProductCategoryPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductCategoryRepository CreateRepository() => new(CreateDbContextFactory());

    private ProductRepository CreateProductRepository() => new(CreateDbContextFactory());

    private async Task<ProductCategory> SeedCategoryAsync(string name)
    {
        var repository = CreateRepository();
        await repository.AddAsync(new ProductCategory { Name = name }, Cancellation);
        return (await repository.GetAllAsync(Cancellation)).Single(c => c.Name == name);
    }

    [Test]
    public async Task A_categorys_own_wastage_is_kept_read_for_its_products_and_held_to_0_to_100()
    {
        var repository = CreateRepository();
        await repository.AddAsync(new ProductCategory { Name = "Breads", WastagePercent = 7.5m }, Cancellation);
        var breads = (await repository.GetAllAsync(Cancellation)).Single();
        var products = CreateProductRepository();
        var inBreads = await AddProductAsync("Pandesal");
        var uncategorized = await AddProductAsync("Cookie");
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Products.Where(p => p.Id == inBreads).ExecuteUpdateAsync(s => s.SetProperty(p => p.CategoryId, breads.Id), Cancellation);
        }

        breads.WastagePercent.Should().Be(7.5m);
        (await products.GetCategoryWastageForProductsAsync([inBreads, uncategorized], Cancellation))
            .Should().BeEquivalentTo(new Dictionary<int, decimal> { [inBreads] = 7.5m }, "a product without its own category's figure isn't in it");

        await repository.UpdateAsync(new ProductCategory { Id = breads.Id, Name = "Breads", WastagePercent = null }, Cancellation);
        (await products.GetCategoryWastageForProductsAsync([inBreads], Cancellation)).Should().BeEmpty("blank is the shop-wide one");

        var tooMuch = () => repository.UpdateAsync(new ProductCategory { Id = breads.Id, Name = "Breads", WastagePercent = 101m }, Cancellation);
        await tooMuch.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task ProductCategoryRepository_AddAsync_GetAllAsync_and_UpdateAsync_round_trip_a_category()
    {
        var repository = CreateRepository();

        await repository.AddAsync(new ProductCategory { Name = "Bread" }, Cancellation);

        var added = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        added.Name.Should().Be("Bread");

        await repository.UpdateAsync(new ProductCategory { Id = added.Id, Name = "Breads" }, Cancellation);

        var updated = (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Subject;
        updated.Name.Should().Be("Breads");
    }

    [Test]
    public async Task ProductCategoryRepository_GetAllAsync_orders_by_name()
    {
        await SeedCategoryAsync("Pastries");
        await SeedCategoryAsync("Breads");
        await SeedCategoryAsync("Cakes");

        var names = (await CreateRepository().GetAllAsync(Cancellation)).Select(c => c.Name);

        names.Should().Equal("Breads", "Cakes", "Pastries");
    }

    [Test]
    public async Task ProductCategoryRepository_UpdateAsync_rejects_a_nonexistent_category()
    {
        var act = () => CreateRepository().UpdateAsync(new ProductCategory { Id = 999_999, Name = "Ghost" }, Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Test]
    public async Task Duplicate_product_category_names_are_rejected()
    {
        await SeedCategoryAsync("Cakes");

        var act = () => CreateRepository().AddAsync(new ProductCategory { Name = "Cakes" }, Cancellation);

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task A_product_category_can_share_a_name_with_a_raw_material_category()
    {
        await using (var dbContext = CreateDbContext())
        {
            dbContext.RawMaterialCategories.Add(new RawMaterialCategory { Name = "Chocolate" });
            await dbContext.SaveChangesAsync(Cancellation);
        }

        var act = () => SeedCategoryAsync("Chocolate");

        await act.Should().NotThrowAsync("the two lists are separate");
    }

    [Test]
    public async Task ProductRepository_saves_a_products_category_and_reads_it_back_with_the_product()
    {
        var cakes = await SeedCategoryAsync("Cakes");
        var repository = CreateProductRepository();

        var id = await repository.SaveAsync(
            new Product { Name = "Chocolate Cake", CategoryId = cakes.Id }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        var byId = await repository.GetByIdAsync(id, Cancellation);
        byId!.CategoryId.Should().Be(cakes.Id);
        byId.Category!.Name.Should().Be("Cakes", "the list groups products by the category's name");
        (await repository.GetAllAsync(Cancellation)).Should().ContainSingle().Which.Category!.Name.Should().Be("Cakes");
    }

    [Test]
    public async Task A_product_needs_no_category_and_can_be_taken_out_of_one()
    {
        var cakes = await SeedCategoryAsync("Cakes");
        var repository = CreateProductRepository();
        var id = await repository.SaveAsync(new Product { Name = "Pandesal" }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        (await repository.GetByIdAsync(id, Cancellation))!.Category.Should().BeNull();

        await repository.UpdateAsync(new Product { Id = id, Name = "Pandesal", CategoryId = cakes.Id }, Cancellation);
        (await repository.GetByIdAsync(id, Cancellation))!.CategoryId.Should().Be(cakes.Id);

        await repository.UpdateAsync(new Product { Id = id, Name = "Pandesal", CategoryId = null }, Cancellation);
        (await repository.GetByIdAsync(id, Cancellation))!.CategoryId.Should().BeNull();
    }

    [Test]
    public async Task Saving_only_a_recipe_keeps_the_products_category()
    {
        var breads = await SeedCategoryAsync("Breads");
        var roleId = await AddRoleAsync("Dough");
        var flourId = await AddIngredientAsync("Flour", costPerGram: 0.05m);
        var productId = await CreateProductRepository().SaveAsync(
            new Product { Name = "Pandesal", CategoryId = breads.Id }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        // As a recipe saved from the product as it was read (SaveRecipeAsync reads it with GetByIdAsync first).
        await SaveRecipeAsync(productId, new RecipeDraft([new NewSubAssemblyDraft(roleId, [(flourId, 1000m)], UsageAuthoredField.WeightPerPiece, 50m)], []));

        (await CreateProductRepository().GetByIdAsync(productId, Cancellation))!.CategoryId.Should().Be(breads.Id);
    }

    [Test]
    public async Task Deleting_a_category_leaves_its_products_uncategorized()
    {
        var cakes = await SeedCategoryAsync("Cakes");
        var id = await CreateProductRepository().SaveAsync(
            new Product { Name = "Chocolate Cake", CategoryId = cakes.Id }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        await CreateRepository().DeleteAsync(cakes.Id, Cancellation);

        (await CreateRepository().GetAllAsync(Cancellation)).Should().BeEmpty();
        var product = (await CreateProductRepository().GetByIdAsync(id, Cancellation))!;
        product.Name.Should().Be("Chocolate Cake", "the product stays");
        product.CategoryId.Should().BeNull();
    }

    [Test]
    public async Task A_clone_goes_in_its_sources_category()
    {
        var cakes = await SeedCategoryAsync("Cakes");
        var sourceId = await CreateProductRepository().SaveAsync(
            new Product { Name = "Chocolate Cake", CategoryId = cakes.Id }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(sourceId, "Chocolate Cake Mini", Cancellation);

        (await CreateProductRepository().GetByIdAsync(cloneId, Cancellation))!.CategoryId.Should().Be(cakes.Id);
    }
}
