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
/// A product's stage, Production or Innovation, and its folder in it: the Products list's places for it. A new product
/// starts in Innovation unless it says otherwise, and only a move (dragging it to another folder, IProductRepository.MoveAsync,
/// or Promote and Send Back, MoveToStageAsync) changes a saved one's. Its category is its own, apart from its folder.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ProductStagePersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<ProductCategory> SeedCategoryAsync(string name)
    {
        var repository = new ProductCategoryRepository(CreateDbContextFactory());
        await repository.AddAsync(new ProductCategory { Name = name }, Cancellation);
        return (await repository.GetAllAsync(Cancellation)).Single(c => c.Name == name);
    }

    private Task<int> SaveNewAsync(string name, int? categoryId = null, int? folderId = null, ProductStage stage = ProductStage.Innovation) =>
        CreateRepository().SaveAsync(
            new Product { Name = name, CategoryId = categoryId, FolderId = folderId, Stage = stage }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private async Task<int> AddFolderAsync(string name, ProductStage stage = ProductStage.Innovation, int? parentId = null)
    {
        var folder = new ProductFolder { Name = name, Stage = stage, ParentId = parentId };
        await new ProductFolderRepository(CreateDbContextFactory()).AddAsync(folder, Cancellation);
        return folder.Id;
    }

    [Test]
    public async Task A_new_product_starts_in_innovation()
    {
        var id = await SaveNewAsync("Ube Cheese Pandesal");

        (await CreateRepository().GetByIdAsync(id, Cancellation))!.Stage.Should().Be(ProductStage.Innovation);
        (await CreateRepository().GetAllAsync(Cancellation)).Should().ContainSingle().Which.Stage.Should().Be(ProductStage.Innovation);
    }

    [Test]
    public async Task A_product_saved_before_stages_existed_reads_as_production()
    {
        // As the migration added the column to the products already there: the row names no stage.
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync("INSERT INTO products (name, is_composite, includes_component_packaging) VALUES ('Pandesal', false, false)", Cancellation);
        }

        (await CreateRepository().GetAllAsync(Cancellation)).Should().ContainSingle().Which.Stage.Should().Be(ProductStage.Production);
    }

    [Test]
    public async Task A_stage_stored_by_innovations_former_name_reads_as_innovation_through_dapper_and_ef()
    {
        // As a copy of the app, the Recipe API or the MCP server built before the rename writes it.
        var id = await SaveNewAsync("Ube Cheese Pandesal");
        var defaults = new CostingDefaultsRepository(CreateDbContextFactory());
        await defaults.SetAsync(new CostingDefaults { NewProductStage = ProductStage.Production }, Cancellation);
        await using (var dbContext = CreateDbContext())
        {
            await dbContext.Database.ExecuteSqlRawAsync("UPDATE products SET stage = 'ResearchAndDevelopment'", Cancellation);
            await dbContext.Database.ExecuteSqlRawAsync("UPDATE costing_defaults SET new_product_stage = 'ResearchAndDevelopment'", Cancellation);
        }

        (await CreateRepository().GetByIdAsync(id, Cancellation))!.Stage.Should().Be(ProductStage.Innovation);
        (await CreateRepository().GetAllAsync(Cancellation)).Should().ContainSingle().Which.Stage.Should().Be(ProductStage.Innovation);
        (await defaults.GetAsync(Cancellation)).NewProductStage.Should().Be(ProductStage.Innovation);
        await using var context = CreateDbContext();
        (await context.Products.SingleAsync(p => p.Id == id, Cancellation)).Stage.Should().Be(ProductStage.Innovation);
    }

    [Test]
    public async Task MoveAsync_sets_the_stage_and_the_folder_and_nothing_else_the_category_included()
    {
        var breads = await SeedCategoryAsync("Breads");
        var cakesFolderId = await AddFolderAsync("Cakes", ProductStage.Production);
        var clock = new TestClock(new DateTime(2026, 10, 1, 9, 0, 0));
        var repository = new ProductRepository(CreateDbContextFactory(clock));
        var id = await repository.SaveAsync(
            new Product { Name = "Mamon", Description = "Soft chiffon cake", CategoryId = breads.Id, QuantityRequired = 6, CurrentPrice = 120m },
            recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        var before = (await repository.GetByIdAsync(id, Cancellation))!;

        clock.Advance(TimeSpan.FromHours(1));
        await repository.MoveAsync([new ProductMove(id, ProductStage.Production, cakesFolderId)], Cancellation);

        var moved = (await repository.GetByIdAsync(id, Cancellation))!;
        moved.Stage.Should().Be(ProductStage.Production);
        moved.FolderId.Should().Be(cakesFolderId);
        moved.CategoryId.Should().Be(breads.Id, "a folder isn't a category");
        moved.Category!.Name.Should().Be("Breads");
        moved.Name.Should().Be("Mamon");
        moved.Description.Should().Be("Soft chiffon cake");
        moved.QuantityRequired.Should().Be(6);
        moved.CurrentPrice.Should().Be(120m);
        before.UpdatedAt.Should().Be(clock.Now - TimeSpan.FromHours(1));
        moved.UpdatedAt.Should().Be(clock.Now, "a move is a change to the product");
    }

    [Test]
    public async Task MoveAsync_can_take_a_product_out_of_its_folder_to_the_top_of_its_stage()
    {
        var breadsId = await AddFolderAsync("Breads");
        var id = await SaveNewAsync("Pandesal", folderId: breadsId);

        await CreateRepository().MoveAsync([new ProductMove(id, ProductStage.Innovation, null)], Cancellation);

        var moved = (await CreateRepository().GetByIdAsync(id, Cancellation))!;
        moved.Stage.Should().Be(ProductStage.Innovation);
        moved.FolderId.Should().BeNull();
    }

    [Test]
    public async Task MoveAsync_into_a_folder_of_the_other_stage_is_refused()
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var id = await SaveNewAsync("Pandesal");

        var act = () => CreateRepository().MoveAsync([new ProductMove(id, ProductStage.Innovation, breadsId)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("That folder is in Production, not Innovation.");
        (await CreateRepository().GetByIdAsync(id, Cancellation))!.FolderId.Should().BeNull();
    }

    [Test]
    public async Task MoveAsync_rejects_a_nonexistent_product()
    {
        var act = () => CreateRepository().MoveAsync([new ProductMove(999_999, ProductStage.Production, null)], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
    }

    [Test]
    public async Task Saving_a_product_keeps_its_stage_and_folder()
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var id = await SaveNewAsync("Pandesal");
        await CreateRepository().MoveAsync([new ProductMove(id, ProductStage.Production, breadsId)], Cancellation);

        // The form's Save holds no stage or folder: the product is written as the form shows it, and stays where it was filed.
        await CreateRepository().SaveAsync(
            new Product { Id = id, Name = "Pandesal Special" }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        await CreateRepository().UpdateAsync(new Product { Id = id, Name = "Pandesal Deluxe" }, Cancellation);

        var saved = (await CreateRepository().GetByIdAsync(id, Cancellation))!;
        saved.Name.Should().Be("Pandesal Deluxe");
        (saved.Stage, saved.FolderId).Should().Be((ProductStage.Production, breadsId));
    }

    [Test]
    public async Task A_clone_starts_in_innovation_in_the_folder_at_its_sources_path_made_if_it_is_not_there()
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var holidayId = await AddFolderAsync("Holiday", ProductStage.Production, breadsId);
        var innovationBreadsId = await AddFolderAsync("breads");
        var sourceId = await SaveNewAsync("Chocolate Cake", folderId: holidayId, stage: ProductStage.Production);
        var looseId = await SaveNewAsync("Loose Loaf", stage: ProductStage.Production);

        var cloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(sourceId, "Chocolate Cake Mini", Cancellation);
        var variantId = await new ProductCloner(CreateDbContextFactory()).CreateVariantAsync(sourceId, "Chocolate Cake Ube", Cancellation);
        var looseCloneId = await new ProductCloner(CreateDbContextFactory()).CloneAsync(looseId, "Loose Loaf Copy", Cancellation);

        var clone = (await CreateRepository().GetByIdAsync(cloneId, Cancellation))!;
        var variant = (await CreateRepository().GetByIdAsync(variantId, Cancellation))!;
        clone.Stage.Should().Be(ProductStage.Innovation);
        variant.Stage.Should().Be(ProductStage.Innovation);
        variant.FolderId.Should().Be(clone.FolderId, "the folder made for the clone is found for the variant");
        var folders = await new ProductFolderRepository(CreateDbContextFactory()).GetAllAsync(Cancellation);
        var made = folders.Single(f => f.Id == clone.FolderId);
        (made.Name, made.Stage, made.ParentId).Should().Be(("Holiday", ProductStage.Innovation, innovationBreadsId), "Innovation's breads was there already, whatever its case");
        (await CreateRepository().GetByIdAsync(looseCloneId, Cancellation))!.FolderId.Should().BeNull("one at the top of its stage goes to the top of Innovation");
    }

    [Test]
    public async Task MoveAsync_moves_several_products_each_to_its_own_folder()
    {
        var breads = await SeedCategoryAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var mamonId = await SaveNewAsync("Mamon", breads.Id);
        var pandesalId = await SaveNewAsync("Pandesal", breads.Id);
        var ensaymadaId = await SaveNewAsync("Ensaymada", breads.Id);

        await CreateRepository().MoveAsync(
            [new ProductMove(mamonId, ProductStage.Production, cakesId), new ProductMove(pandesalId, ProductStage.Production, null)], Cancellation);

        var products = (await CreateRepository().GetAllAsync(Cancellation)).ToDictionary(p => p.Id);
        (products[mamonId].Stage, products[mamonId].FolderId).Should().Be((ProductStage.Production, cakesId));
        (products[pandesalId].Stage, products[pandesalId].FolderId).Should().Be((ProductStage.Production, (int?)null));
        products[ensaymadaId].Stage.Should().Be(ProductStage.Innovation, "it wasn't moved");
        products.Values.Should().OnlyContain(p => p.CategoryId == breads.Id);
    }

    [Test]
    public async Task MoveAsync_to_a_folder_that_no_longer_exists_is_refused_and_moves_nothing()
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var mamonId = await SaveNewAsync("Mamon");
        var pandesalId = await SaveNewAsync("Pandesal");

        var act = () => CreateRepository().MoveAsync(
            [new ProductMove(mamonId, ProductStage.Production, breadsId), new ProductMove(pandesalId, ProductStage.Production, breadsId + 999)], Cancellation);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("The folder to move it to no longer exists.");
        (await CreateRepository().GetAllAsync(Cancellation)).Should().OnlyContain(p => p.Stage == ProductStage.Innovation && p.FolderId == null);
    }

    [Test]
    public async Task MoveToStageAsync_files_each_product_at_its_folders_path_in_the_other_stage_and_says_what_it_made()
    {
        var clock = new TestClock(new DateTime(2026, 11, 12, 9, 0, 0));
        var repository = new ProductRepository(CreateDbContextFactory(clock));
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", ProductStage.Innovation, breadsId);
        var productionBreadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var bibingkaId = await SaveNewAsync("Bibingka", folderId: holidayId);
        var puto = await SaveNewAsync("Puto", folderId: holidayId);
        var mamonId = await SaveNewAsync("Mamon", folderId: breadsId);
        var looseId = await SaveNewAsync("Loose Loaf");
        var alreadyId = await SaveNewAsync("Pandesal", folderId: productionBreadsId, stage: ProductStage.Production);
        var alreadyBefore = (await repository.GetByIdAsync(alreadyId, Cancellation))!.UpdatedAt;
        clock.Advance(TimeSpan.FromHours(1));

        var moved = await repository.MoveToStageAsync([bibingkaId, puto, mamonId, looseId, alreadyId], ProductStage.Production, Cancellation);

        var made = moved.MadeFolderIds.Should().ContainSingle("Holiday is made once, in Breads, which was there").Subject;
        var folders = await new ProductFolderRepository(CreateDbContextFactory()).GetAllAsync(Cancellation);
        folders.Single(f => f.Id == made).Should().Match<ProductFolder>(f => f.Name == "Holiday" && f.ParentId == productionBreadsId && f.Stage == ProductStage.Production);
        moved.Moves.Should().Equal(
            new ProductMove(bibingkaId, ProductStage.Production, made),
            new ProductMove(puto, ProductStage.Production, made),
            new ProductMove(mamonId, ProductStage.Production, productionBreadsId),
            new ProductMove(looseId, ProductStage.Production, null));
        var products = (await repository.GetAllAsync(Cancellation)).ToDictionary(p => p.Id);
        (products[bibingkaId].Stage, products[bibingkaId].FolderId).Should().Be((ProductStage.Production, made));
        (products[looseId].Stage, products[looseId].FolderId).Should().Be((ProductStage.Production, (int?)null));
        products[mamonId].UpdatedAt.Should().Be(clock.Now);
        products[alreadyId].UpdatedAt.Should().Be(alreadyBefore, "one in Production already is left as it is");
    }

    [Test]
    public async Task A_new_product_filed_in_a_folder_goes_in_the_folders_stage_or_at_the_top_when_the_folder_is_gone()
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);

        var filedId = await SaveNewAsync("Pandesal", folderId: breadsId, stage: ProductStage.Innovation);
        var goneId = await SaveNewAsync("Ensaymada", folderId: breadsId + 999, stage: ProductStage.Innovation);

        var filed = (await CreateRepository().GetByIdAsync(filedId, Cancellation))!;
        (filed.Stage, filed.FolderId).Should().Be((ProductStage.Production, breadsId), "a product goes in its folder's stage");
        var gone = (await CreateRepository().GetByIdAsync(goneId, Cancellation))!;
        (gone.Stage, gone.FolderId).Should().Be((ProductStage.Innovation, (int?)null));
    }

    [Test]
    public async Task MoveAsync_with_a_product_that_no_longer_exists_moves_none_of_the_others()
    {
        var mamonId = await SaveNewAsync("Mamon");

        var act = () => CreateRepository().MoveAsync(
            [new ProductMove(mamonId, ProductStage.Production, null), new ProductMove(999_999, ProductStage.Production, null)], Cancellation);

        await act.Should().ThrowAsync<BusinessRuleException>();
        (await CreateRepository().GetByIdAsync(mamonId, Cancellation))!.Stage.Should().Be(ProductStage.Innovation);
    }

    [Test]
    public async Task A_new_product_saved_with_a_stage_goes_in_it()
    {
        var id = await CreateRepository().SaveAsync(
            new Product { Name = "Pan de Sal", Stage = ProductStage.Production }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

        (await CreateRepository().GetByIdAsync(id, Cancellation))!.Stage.Should().Be(ProductStage.Production);

        // A saved one's stage is only ever a move's to change.
        await CreateRepository().SaveAsync(
            new Product { Id = id, Name = "Pan de Sal", Stage = ProductStage.Innovation }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);
        (await CreateRepository().GetByIdAsync(id, Cancellation))!.Stage.Should().Be(ProductStage.Production);
    }

    [Test]
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
