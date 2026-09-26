using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;

namespace TinapayanRMS.Tests.Persistence;

/// <summary>
/// A product's stage, Production or Research and Development: the Products list's top-level folder for it. A new product
/// starts in Research and Development, and only a move (dragging it to another folder, IProductRepository.MoveAsync)
/// changes it.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductStagePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<ProductCategory> SeedCategoryAsync(string name)
    {
        var repository = new ProductCategoryRepository(CreateDbContextFactory());
        await repository.AddAsync(new ProductCategory { Name = name }, Cancellation);
        return (await repository.GetAllAsync(Cancellation)).Single(c => c.Name == name);
    }

    private Task<int> SaveNewAsync(string name, int? categoryId = null) =>
        CreateRepository().SaveAsync(new Product { Name = name, CategoryId = categoryId }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    [Fact]
    public async Task A_new_product_starts_in_research_and_development()
    {
        var id = await SaveNewAsync("Ube Cheese Pandesal");

        (await CreateRepository().GetByIdAsync(id, Cancellation))!.Stage.Should().Be(ProductStage.ResearchAndDevelopment);
        (await CreateRepository().GetAllAsync(Cancellation)).Should().ContainSingle().Which.Stage.Should().Be(ProductStage.ResearchAndDevelopment);
    }

    [Fact]
    public async Task A_product_saved_before_stages_existed_reads_as_production()
    {
        // As the migration added the column to the products already there: the row names no stage.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync("INSERT INTO Products (Name) VALUES (N'Pandesal')", Cancellation);
        }

        (await CreateRepository().GetAllAsync(Cancellation)).Should().ContainSingle().Which.Stage.Should().Be(ProductStage.Production);
    }

    [Fact]
    public async Task MoveAsync_sets_the_stage_and_the_category_and_nothing_else()
    {
        var breads = await SeedCategoryAsync("Breads");
        var cakes = await SeedCategoryAsync("Cakes");
        var repository = CreateRepository();
        var id = await repository.SaveAsync(
            new Product { Name = "Mamon", Description = "Soft chiffon cake", CategoryId = breads.Id, QuantityRequired = 6, CurrentPrice = 120m },
            recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        var before = (await repository.GetByIdAsync(id, Cancellation))!;

        await repository.MoveAsync([new ProductMove(id, ProductStage.Production, cakes.Id)], Cancellation);

        var moved = (await repository.GetByIdAsync(id, Cancellation))!;
        moved.Stage.Should().Be(ProductStage.Production);
        moved.CategoryId.Should().Be(cakes.Id);
        moved.Category!.Name.Should().Be("Cakes");
        moved.Name.Should().Be("Mamon");
        moved.Description.Should().Be("Soft chiffon cake");
        moved.QuantityRequired.Should().Be(6);
        moved.CurrentPrice.Should().Be(120m);
        moved.UpdatedAt.Should().BeOnOrAfter(before.UpdatedAt!.Value, "a move is a change to the product");
    }

    [Fact]
    public async Task MoveAsync_can_take_a_product_out_of_its_category()
    {
        var breads = await SeedCategoryAsync("Breads");
        var id = await SaveNewAsync("Pandesal", breads.Id);

        await CreateRepository().MoveAsync([new ProductMove(id, ProductStage.ResearchAndDevelopment, null)], Cancellation);

        var moved = (await CreateRepository().GetByIdAsync(id, Cancellation))!;
        moved.Stage.Should().Be(ProductStage.ResearchAndDevelopment);
        moved.CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task MoveAsync_rejects_a_nonexistent_product()
    {
        var act = () => CreateRepository().MoveAsync([new ProductMove(999_999, ProductStage.Production, null)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Saving_a_product_keeps_its_stage()
    {
        var id = await SaveNewAsync("Pandesal");
        await CreateRepository().MoveAsync([new ProductMove(id, ProductStage.Production, null)], Cancellation);

        // The form's Save holds no stage: the product is written as the form shows it, and stays where it was filed.
        await CreateRepository().SaveAsync(
            new Product { Id = id, Name = "Pandesal Special" }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        await CreateRepository().UpdateAsync(new Product { Id = id, Name = "Pandesal Deluxe" }, Cancellation);

        var saved = (await CreateRepository().GetByIdAsync(id, Cancellation))!;
        saved.Name.Should().Be("Pandesal Deluxe");
        saved.Stage.Should().Be(ProductStage.Production);
    }

    [Fact]
    public async Task A_clone_starts_in_research_and_development_whatever_its_sources_stage()
    {
        var sourceId = await SaveNewAsync("Chocolate Cake");
        await CreateRepository().MoveAsync([new ProductMove(sourceId, ProductStage.Production, null)], Cancellation);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(sourceId, "Chocolate Cake Mini", Cancellation);
        var variantId = await new ProductCloner(CreateDbContextFactory()).CreateVariantAsync(sourceId, "Chocolate Cake Ube", Cancellation);

        (await CreateRepository().GetByIdAsync(cloneId, Cancellation))!.Stage.Should().Be(ProductStage.ResearchAndDevelopment);
        (await CreateRepository().GetByIdAsync(variantId, Cancellation))!.Stage.Should().Be(ProductStage.ResearchAndDevelopment);
    }

    [Fact]
    public async Task MoveAsync_moves_several_products_each_to_its_own_folder()
    {
        var breads = await SeedCategoryAsync("Breads");
        var cakes = await SeedCategoryAsync("Cakes");
        var mamonId = await SaveNewAsync("Mamon", cakes.Id);
        var pandesalId = await SaveNewAsync("Pandesal", breads.Id);
        var ensaymadaId = await SaveNewAsync("Ensaymada", breads.Id);

        await CreateRepository().MoveAsync(
            [new ProductMove(mamonId, ProductStage.Production, cakes.Id), new ProductMove(pandesalId, ProductStage.Production, null)], Cancellation);

        var products = (await CreateRepository().GetAllAsync(Cancellation)).ToDictionary(p => p.Id);
        (products[mamonId].Stage, products[mamonId].CategoryId).Should().Be((ProductStage.Production, cakes.Id));
        (products[pandesalId].Stage, products[pandesalId].CategoryId).Should().Be((ProductStage.Production, (int?)null));
        (products[ensaymadaId].Stage, products[ensaymadaId].CategoryId).Should().Be((ProductStage.ResearchAndDevelopment, breads.Id), "it wasn't moved");
    }

    [Fact]
    public async Task MoveAsync_to_a_category_that_no_longer_exists_is_refused_and_moves_nothing()
    {
        var breads = await SeedCategoryAsync("Breads");
        var mamonId = await SaveNewAsync("Mamon");
        var pandesalId = await SaveNewAsync("Pandesal");

        var act = () => CreateRepository().MoveAsync(
            [new ProductMove(mamonId, ProductStage.Production, breads.Id), new ProductMove(pandesalId, ProductStage.Production, breads.Id + 999)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no longer exists*");
        (await CreateRepository().GetAllAsync(Cancellation)).Should().OnlyContain(p => p.Stage == ProductStage.ResearchAndDevelopment && p.CategoryId == null);
    }

    [Fact]
    public async Task MoveAsync_with_a_product_that_no_longer_exists_moves_none_of_the_others()
    {
        var mamonId = await SaveNewAsync("Mamon");

        var act = () => CreateRepository().MoveAsync(
            [new ProductMove(mamonId, ProductStage.Production, null), new ProductMove(999_999, ProductStage.Production, null)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await CreateRepository().GetByIdAsync(mamonId, Cancellation))!.Stage.Should().Be(ProductStage.ResearchAndDevelopment);
    }

    [Fact]
    public async Task A_new_product_saved_with_a_stage_goes_in_it()
    {
        var id = await CreateRepository().SaveAsync(
            new Product { Name = "Pan de Sal", Stage = ProductStage.Production }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        (await CreateRepository().GetByIdAsync(id, Cancellation))!.Stage.Should().Be(ProductStage.Production);

        // A saved one's stage is only ever a move's to change.
        await CreateRepository().SaveAsync(
            new Product { Id = id, Name = "Pan de Sal", Stage = ProductStage.ResearchAndDevelopment }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        (await CreateRepository().GetByIdAsync(id, Cancellation))!.Stage.Should().Be(ProductStage.Production);
    }

    [Fact]
    public async Task GetByIdsAsync_reads_only_the_products_asked_for()
    {
        var breads = await SeedCategoryAsync("Breads");
        var mamonId = await SaveNewAsync("Mamon");
        var pandesalId = await SaveNewAsync("Pandesal", breads.Id);
        await SaveNewAsync("Ensaymada");

        var read = await CreateRepository().GetByIdsAsync([pandesalId, mamonId, 999_999], Cancellation);

        read.Select(p => p.Name).Should().Equal("Mamon", "Pandesal");
        read.Single(p => p.Id == pandesalId).Category!.Name.Should().Be("Breads");
        (await CreateRepository().GetByIdsAsync([], Cancellation)).Should().BeEmpty();
    }
}
