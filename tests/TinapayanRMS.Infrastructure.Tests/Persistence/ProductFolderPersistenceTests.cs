using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// The Products list's folders (ProductFolder): made, named, nested, moved and deleted as in a file explorer, each in one
/// stage with everything in it, a name unique beside it, and never inside itself. Deleting one deletes no product.
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ProductFolderPersistenceTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    private ProductFolderRepository CreateRepository() => new(CreateDbContextFactory());

    private async Task<int> AddFolderAsync(string name, ProductStage stage = ProductStage.Production, int? parentId = null)
    {
        var folder = new ProductFolder { Name = name, Stage = stage, ParentId = parentId };
        await CreateRepository().AddAsync(folder, Cancellation);
        return folder.Id;
    }

    private Task<int> AddProductInAsync(string name, int? folderId, ProductStage stage = ProductStage.Production) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, FolderId = folderId, Stage = stage }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private async Task<Product> ProductAsync(int id)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Products.AsNoTracking().SingleAsync(p => p.Id == id, Cancellation);
    }

    private async Task<ProductFolder?> FolderAsync(int id) =>
        (await CreateRepository().GetAllAsync(Cancellation)).SingleOrDefault(f => f.Id == id);

    [Test]
    public async Task A_folder_is_added_at_the_top_of_a_stage_or_inside_another_in_that_ones_stage_its_name_trimmed()
    {
        var breadsId = await AddFolderAsync("  Breads ");
        var holiday = new ProductFolder { Name = "Holiday", Stage = ProductStage.Innovation, ParentId = breadsId };

        await CreateRepository().AddAsync(holiday, Cancellation);

        holiday.Stage.Should().Be(ProductStage.Production, "a folder is in its parent's stage");
        var folders = await CreateRepository().GetAllAsync(Cancellation);
        folders.Select(f => (f.Name, f.Stage, f.ParentId)).Should().Equal(
            ("Breads", ProductStage.Production, (int?)null), ("Holiday", ProductStage.Production, breadsId));
    }

    [Test]
    public async Task A_name_is_unique_beside_the_folder_whatever_its_case_but_free_in_another_place()
    {
        var breadsId = await AddFolderAsync("Breads");

        var sameCase = () => AddFolderAsync("breads");
        await sameCase.Should().ThrowAsync<BusinessRuleException>().WithMessage("Production already has a folder named breads.");

        await AddFolderAsync("Breads", ProductStage.Innovation);
        var inside = await AddFolderAsync("Breads", parentId: breadsId);
        var insideAgain = () => AddFolderAsync("BREADS", parentId: breadsId);
        await insideAgain.Should().ThrowAsync<BusinessRuleException>().WithMessage("Production › Breads already has a folder named BREADS.");

        var empty = () => AddFolderAsync("   ");
        await empty.Should().ThrowAsync<BusinessRuleException>().WithMessage("Every folder needs a name.");
        var orphan = () => AddFolderAsync("Lost", parentId: inside + 999);
        await orphan.Should().ThrowAsync<BusinessRuleException>();
        (await CreateRepository().GetAllAsync(Cancellation)).Should().HaveCount(3);
    }

    [Test]
    public async Task The_database_holds_a_name_unique_at_the_top_of_a_stage_too()
    {
        // The repository checks first; the unique index is there for what gets past it, a null parent included.
        await AddFolderAsync("Breads");
        await using var dbContext = CreateDbContext();
        dbContext.ProductFolders.Add(new ProductFolder { Name = "BREADS", Stage = ProductStage.Production });

        var save = () => dbContext.SaveChangesAsync(Cancellation);

        await save.Should().ThrowAsync<DbUpdateException>();
    }

    [Test]
    public async Task Rename_renames_the_folder_alone_and_refuses_a_name_taken_beside_it()
    {
        var breadsId = await AddFolderAsync("Breads");
        await AddFolderAsync("Cakes");
        var pandesalId = await AddProductInAsync("Pandesal", breadsId);

        await CreateRepository().RenameAsync(breadsId, " Bread ", Cancellation);
        var taken = () => CreateRepository().RenameAsync(breadsId, "cakes", Cancellation);

        await taken.Should().ThrowAsync<BusinessRuleException>().WithMessage("Production already has a folder named cakes.");
        (await FolderAsync(breadsId))!.Name.Should().Be("Bread");
        (await ProductAsync(pandesalId)).FolderId.Should().Be(breadsId);
        await CreateRepository().RenameAsync(breadsId, "BREAD", Cancellation);
        (await FolderAsync(breadsId))!.Name.Should().Be("BREAD", "its own name in another case is free");
    }

    [Test]
    public async Task A_move_within_a_stage_moves_the_folder_and_changes_no_product()
    {
        var clock = new TestClock(new DateTime(2026, 11, 12, 9, 0, 0));
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes");
        var pandesalId = await AddProductInAsync("Pandesal", breadsId);
        var before = (await ProductAsync(pandesalId)).UpdatedAt;

        var restaged = await new ProductFolderRepository(CreateDbContextFactory(clock)).MoveAsync(
            new ProductFolderMove(breadsId, ProductStage.Production, cakesId), Cancellation);

        restaged.Should().BeEmpty();
        (await FolderAsync(breadsId))!.ParentId.Should().Be(cakesId);
        var pandesal = await ProductAsync(pandesalId);
        (pandesal.FolderId, pandesal.UpdatedAt).Should().Be((breadsId, before));
    }

    [Test]
    public async Task A_move_to_the_other_stage_takes_every_folder_and_product_in_it_deleted_ones_too()
    {
        var clock = new TestClock(new DateTime(2026, 11, 12, 9, 0, 0));
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", parentId: breadsId);
        var cakesId = await AddFolderAsync("Cakes");
        var pandesalId = await AddProductInAsync("Pandesal", breadsId);
        var bibingkaId = await AddProductInAsync("Bibingka", holidayId);
        var mamonId = await AddProductInAsync("Mamon", cakesId);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(bibingkaId, Cancellation);
        clock.Advance(TimeSpan.FromDays(1));

        var restaged = await new ProductFolderRepository(CreateDbContextFactory(clock)).MoveAsync(
            new ProductFolderMove(breadsId, ProductStage.Innovation, null), Cancellation);

        restaged.Should().BeEquivalentTo([pandesalId, bibingkaId]);
        (await FolderAsync(breadsId))!.Stage.Should().Be(ProductStage.Innovation);
        (await FolderAsync(holidayId))!.Should().Match<ProductFolder>(f => f.Stage == ProductStage.Innovation && f.ParentId == breadsId);
        var pandesal = await ProductAsync(pandesalId);
        (pandesal.Stage, pandesal.FolderId, pandesal.UpdatedAt).Should().Be((ProductStage.Innovation, breadsId, (DateTime?)clock.Now));
        (await ProductAsync(bibingkaId)).Stage.Should().Be(ProductStage.Innovation, "a deleted one stays in its folder's stage");
        (await ProductAsync(mamonId)).Stage.Should().Be(ProductStage.Production, "one in another folder isn't touched");
        (await FolderAsync(cakesId))!.Stage.Should().Be(ProductStage.Production);
    }

    [Test]
    public async Task A_move_inside_itself_onto_a_name_taken_or_into_a_folder_gone_or_of_the_other_stage_is_refused()
    {
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", parentId: breadsId);
        var otherHolidayId = await AddFolderAsync("holiday");
        var trialsId = await AddFolderAsync("Trials", ProductStage.Innovation);

        var intoItself = () => CreateRepository().MoveAsync(new ProductFolderMove(breadsId, ProductStage.Production, breadsId), Cancellation);
        var intoItsOwn = () => CreateRepository().MoveAsync(new ProductFolderMove(breadsId, ProductStage.Production, holidayId), Cancellation);
        var nameTaken = () => CreateRepository().MoveAsync(new ProductFolderMove(holidayId, ProductStage.Production, null), Cancellation);
        var gone = () => CreateRepository().MoveAsync(new ProductFolderMove(holidayId, ProductStage.Production, trialsId + 999), Cancellation);
        var otherStage = () => CreateRepository().MoveAsync(new ProductFolderMove(holidayId, ProductStage.Production, trialsId), Cancellation);

        await intoItself.Should().ThrowAsync<BusinessRuleException>().WithMessage("A folder can't go inside itself.");
        await intoItsOwn.Should().ThrowAsync<BusinessRuleException>().WithMessage("A folder can't go inside itself.");
        await nameTaken.Should().ThrowAsync<BusinessRuleException>().WithMessage("Production already has a folder named Holiday.");
        await gone.Should().ThrowAsync<BusinessRuleException>().WithMessage("The folder to move it to no longer exists.");
        await otherStage.Should().ThrowAsync<BusinessRuleException>().WithMessage("That folder is in Innovation, not Production.");
        (await FolderAsync(holidayId))!.ParentId.Should().Be(breadsId);
        (await FolderAsync(otherHolidayId))!.ParentId.Should().BeNull();
    }

    [Test]
    public async Task Delete_deletes_the_folder_alone_and_moves_what_it_holds_up_a_level()
    {
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", parentId: breadsId);
        var sameNameId = await AddFolderAsync("Holiday", parentId: holidayId);
        var bibingkaId = await AddProductInAsync("Bibingka", holidayId);
        var oldId = await AddProductInAsync("Old Bibingka", holidayId);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(oldId, Cancellation);

        await CreateRepository().DeleteAsync(holidayId, Cancellation);

        (await FolderAsync(holidayId)).Should().BeNull();
        (await FolderAsync(sameNameId))!.ParentId.Should().Be(breadsId, "a folder in it of its own name takes its place");
        (await ProductAsync(bibingkaId)).FolderId.Should().Be(breadsId);
        (await ProductAsync(oldId)).FolderId.Should().Be(breadsId, "a deleted one too, for its restore");

        await CreateRepository().DeleteAsync(breadsId, Cancellation);
        (await ProductAsync(bibingkaId)).Should().Match<Product>(p => p.FolderId == null && p.Stage == ProductStage.Production && !p.IsDeleted);
        (await FolderAsync(sameNameId))!.ParentId.Should().BeNull();
    }

    [Test]
    public async Task Delete_is_refused_when_a_folder_in_it_has_a_name_taken_a_level_up()
    {
        var breadsId = await AddFolderAsync("Breads");
        await AddFolderAsync("Holiday");
        var innerId = await AddFolderAsync("holiday", parentId: breadsId);
        var pandesalId = await AddProductInAsync("Pandesal", breadsId);

        var delete = () => CreateRepository().DeleteAsync(breadsId, Cancellation);

        await delete.Should().ThrowAsync<BusinessRuleException>().WithMessage("Production already has a folder named holiday.");
        (await FolderAsync(breadsId)).Should().NotBeNull();
        (await FolderAsync(innerId))!.ParentId.Should().Be(breadsId);
        (await ProductAsync(pandesalId)).FolderId.Should().Be(breadsId);
    }

    [Test]
    public async Task DeleteIfEmptyAsync_deletes_the_empty_ones_deepest_first_and_leaves_any_holding_something()
    {
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", parentId: breadsId);
        var cakesId = await AddFolderAsync("Cakes");
        var keptId = await AddFolderAsync("Kept");
        await AddProductInAsync("Mamon", cakesId);

        await CreateRepository().DeleteIfEmptyAsync([breadsId, holidayId, cakesId, keptId + 999], Cancellation);

        (await CreateRepository().GetAllAsync(Cancellation)).Select(f => f.Id).Should().BeEquivalentTo([cakesId, keptId]);
    }
}
