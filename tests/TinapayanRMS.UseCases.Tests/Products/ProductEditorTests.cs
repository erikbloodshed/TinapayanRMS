using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UseCases.Products;
using TinapayanRMS.UseCases.Tests.Fixtures;

namespace TinapayanRMS.UseCases.Tests.Products;

/// <summary>
/// The Products use cases on the real repositories: what the editor refuses, what a Save writes and reads back with its
/// version, a clone's and a variant's first versions, the list's renames and folders, the Category picker's new category,
/// and what the queries read. The screen's own tests (Presentation.Tests' Products) drive them through the forms.
/// </summary>
[NotInParallel(DatabaseCollection.Name)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.Name)]
public class ProductEditorTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public void A_product_needs_a_name_and_a_piece_and_its_costing_sound_percentages()
    {
        var editor = CreateEditor();

        editor.Invoking(e => e.EnsureValid(new Product { Name = "", QuantityRequired = 1 })).Should().Throw<BusinessRuleException>();
        editor.Invoking(e => e.EnsureValid(new Product { Name = "Pandesal", QuantityRequired = 0 }))
            .Should().Throw<BusinessRuleException>().WithMessage("A product has to hold at least one piece. Enter 1 for one sold by the piece.");
        editor.Invoking(e => e.EnsureValid(new Product { Name = "Pandesal", QuantityRequired = 1 })).Should().NotThrow();
        editor.Invoking(e => e.EnsureValid(new CostingParameters { LaborPercent = -5m })).Should().Throw<BusinessRuleException>();
        editor.Invoking(e => e.EnsureValid(new CostingParameters())).Should().NotThrow();
    }

    [Test]
    public async Task A_save_is_refused_as_the_checks_refuse_and_writes_nothing()
    {
        var editor = CreateEditor();

        await editor.Awaiting(e => e.SaveAsync(Request(new Product { Name = "Pandesal", QuantityRequired = 0 }), DateTime.Now, Cancellation))
            .Should().ThrowAsync<BusinessRuleException>();
        await editor.Awaiting(e => e.SaveAsync(Request(new Product { Name = "Pandesal", QuantityRequired = 1 }, new CostingParameters { LaborPercent = -5m }), DateTime.Now, Cancellation))
            .Should().ThrowAsync<BusinessRuleException>();

        (await CreateQueries().ReadLoadAsync([], [], [], Cancellation)).Products.Should().BeEmpty();
    }

    [Test]
    public async Task A_new_product_is_saved_read_back_and_given_its_first_version()
    {
        var saved = await CreateEditor().SaveAsync(Request(new Product { Name = "Pandesal", QuantityRequired = 1, CurrentPrice = 5m }), DateTime.Now, Cancellation);

        saved.Product.Name.Should().Be("Pandesal");
        var history = (await CreateQueries().ReadHistoryAsync(saved.Id, Cancellation))!;
        history.Product.CurrentPrice.Should().Be(5m);
        history.Versions.Should().ContainSingle();
    }

    [Test]
    public async Task A_clone_and_a_variant_say_where_they_came_from()
    {
        var editor = CreateEditor();
        var source = await editor.SaveAsync(Request(new Product { Name = "Ensaymada Big", QuantityRequired = 1 }), DateTime.Now, Cancellation);

        var cloneId = await editor.MakeFromAsync(source.Id, "Ensaymada Copy", isVariant: false, DateTime.Now, Cancellation);
        var variantId = await editor.MakeFromAsync(source.Id, "Ensaymada Small", isVariant: true, DateTime.Now, Cancellation);

        var queries = CreateQueries();
        (await queries.ReadHistoryAsync(cloneId, Cancellation))!.Versions.Should().ContainSingle()
            .Which.Note.Should().StartWith(ProductEditor.ClonedFrom).And.Contain("Ensaymada Big");
        (await queries.ReadHistoryAsync(variantId, Cancellation))!.Versions.Should().ContainSingle()
            .Which.Note.Should().StartWith(ProductEditor.VariantOf).And.Contain("Ensaymada Big");
    }

    [Test]
    public async Task A_deleted_product_reads_as_deleted_and_comes_back_on_restore()
    {
        var editor = CreateEditor();
        var id = await AddProductAsync("Pandesal");
        var queries = CreateQueries();

        await editor.DeleteAsync(id, Cancellation);
        (await queries.ReadLoadAsync([], [], [], Cancellation)).DeletedProducts.Select(p => p.Id).Should().Equal(id);

        await editor.RestoreAsync(id, Cancellation);
        var load = await queries.ReadLoadAsync([], [], [], Cancellation);
        load.DeletedProducts.Should().BeEmpty();
        load.Products.Select(p => p.Id).Should().Equal(id);

        await editor.DeletePermanentlyAsync(id, Cancellation);
        (await queries.ReadAsync(id, Cancellation)).Should().BeNull();
        (await queries.ReadHistoryAsync(id, Cancellation)).Should().BeNull();
    }

    [Test]
    public async Task A_rename_is_refused_over_a_save_made_since_the_form_took_the_product()
    {
        var saved = await CreateEditor().SaveAsync(Request(new Product { Name = "Pandesal", QuantityRequired = 1 }), DateTime.Now, Cancellation);
        var (id, opened) = (saved.Id, saved.Product.UpdatedAt!.Value);
        var queries = CreateQueries();
        var listEditor = CreateListEditor();
        await listEditor.RenameAsync(id, "Pan de Sal", [new SavedVersionCheck(id, opened)], Cancellation);

        await listEditor.Awaiting(e => e.RenameAsync(id, "Pandesal Classic", [new SavedVersionCheck(id, opened)], Cancellation))
            .Should().ThrowAsync<BusinessRuleException>();
        (await queries.ReadAsync(id, Cancellation))!.Name.Should().Be("Pan de Sal");
    }

    [Test]
    public async Task A_folder_is_added_renamed_moved_and_deleted_as_the_list_reads_them()
    {
        var listEditor = CreateListEditor();
        var queries = CreateQueries();
        var breads = new ProductFolder { Name = "Breads", Stage = ProductStage.Production };
        var rolls = new ProductFolder { Name = "Rolls", Stage = ProductStage.Production };

        await listEditor.AddFolderAsync(breads, Cancellation);
        await listEditor.AddFolderAsync(rolls, Cancellation);
        breads.Id.Should().BePositive("the folder is given its saved id");
        await listEditor.RenameFolderAsync(breads.Id, "Bread", Cancellation);
        await listEditor.MoveFolderAsync(new ProductFolderMove(rolls.Id, ProductStage.Production, breads.Id), [], Cancellation);
        (await queries.ReadFoldersAsync(Cancellation)).Select(f => (f.Name, f.ParentId)).Should().BeEquivalentTo([("Bread", (int?)null), ("Rolls", breads.Id)]);

        // Deleting a folder deletes nothing in it: what it held goes up into the folder it was in.
        await listEditor.DeleteFolderAsync(breads.Id, Cancellation);
        (await queries.ReadFoldersAsync(Cancellation)).Select(f => (f.Name, f.ParentId)).Should().Equal(("Rolls", (int?)null));

        await listEditor.DeleteFoldersIfEmptyAsync([rolls.Id], Cancellation);
        (await queries.ReadFoldersAsync(Cancellation)).Should().BeEmpty();
    }

    [Test]
    public async Task A_product_moved_to_a_folder_and_promoted_is_filed_as_the_list_reads_it()
    {
        var listEditor = CreateListEditor();
        var queries = CreateQueries();
        var id = await AddProductAsync("Ube Pandesal");
        var folder = new ProductFolder { Name = "Breads", Stage = ProductStage.Innovation };
        await listEditor.AddFolderAsync(folder, Cancellation);

        await listEditor.MoveAsync([new ProductMove(id, ProductStage.Innovation, folder.Id)], [], Cancellation);
        (await queries.ReadAsync(id, Cancellation))!.FolderId.Should().Be(folder.Id);

        var promoted = await listEditor.MoveToStageAsync([id], ProductStage.Production, [], Cancellation);

        promoted.Moves.Select(m => m.ProductId).Should().Equal(id);
        promoted.MadeFolderIds.Should().ContainSingle("Production had no Breads folder, so the Promote made one");
        var moved = (await queries.ReadAsync(id, Cancellation))!;
        moved.Stage.Should().Be(ProductStage.Production);
        moved.FolderId.Should().Be(promoted.MadeFolderIds[0]);
    }

    [Test]
    public async Task The_category_picker_adds_a_category_with_a_name_and_reads_it_back()
    {
        var categoryEditor = new ProductCategoryEditor(new ProductCategoryRepository(CreateDbContextFactory()), new ProductCategoryValidator());

        categoryEditor.Invoking(e => e.EnsureValid("")).Should().Throw<BusinessRuleException>().WithMessage("Name is required.");
        await categoryEditor.Awaiting(e => e.AddAsync("", Cancellation)).Should().ThrowAsync<BusinessRuleException>();
        var added = await categoryEditor.AddAsync("Breads", Cancellation);

        (await CreateQueries().ReadCategoriesAsync(Cancellation)).Should().ContainSingle(c => c.Id == added.Id && c.Name == "Breads");
    }

    [Test]
    public async Task Make_In_House_Ingredient_is_offered_the_Made_categories_alone()
    {
        var madeId = await MadeCategoryIdAsync();
        await AddPackagingCategoryAsync();

        (await CreateQueries().ReadMadeCategoriesAsync(Cancellation)).Select(c => c.Id).Should().Equal(madeId);
    }

    private static ProductSaveRequest Request(Product product, CostingParameters? costing = null) =>
        new(product, costing ?? new CostingParameters(), CostingChanged: false, Recipe: null, PackagingLink: null, PackagingLines: null, BulkPackaging: null, ChangesSharing: false);

    private ProductEditor CreateEditor()
    {
        var factory = CreateDbContextFactory();
        return new ProductEditor(
            new ProductRepository(factory),
            new ProductVersionRepository(factory),
            new ProductCloner(factory),
            VersionRecorders.CreateSaves(factory, CreateUnitOfWork()),
            new ProductValidator(),
            new CostingParametersValidator());
    }

    private ProductListEditor CreateListEditor()
    {
        var factory = CreateDbContextFactory();
        return new ProductListEditor(new ProductRepository(factory), new ProductFolderRepository(factory));
    }

    private ProductQueries CreateQueries()
    {
        var factory = CreateDbContextFactory();
        return new ProductQueries(
            new ProductRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductFolderRepository(factory),
            new ProductVersionRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory),
            new PackagingLineRepository(factory),
            VersionRecorders.CreateReader(factory, CreateUnitOfWork()));
    }
}
