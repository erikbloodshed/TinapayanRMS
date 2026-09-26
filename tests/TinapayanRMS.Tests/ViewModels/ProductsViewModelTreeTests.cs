using System.Diagnostics;
using System.Reactive.Linq;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Tests.ViewModels;

/// <summary>
/// The Products list's right-click menus, its keys and its selection: what each menu offers a product, several products, a
/// folder and the list itself, and what each item does, run as the menu runs it. Moves and renames save at once; the
/// notice of a move can take it back; the folders left open open again next time.
/// </summary>
[Collection(DatabaseCollection.Name)]
public class ProductsViewModelTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task A_products_menu_moves_it_to_the_other_stage_or_any_folder_and_offers_what_else_can_be_done_with_it()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        await AddProductCategoryAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await LoadedViewModelAsync();
        var leaf = viewModel.ProductTree.Find(mamonId)!;
        viewModel.SelectedTreeNode = leaf;

        var menu = viewModel.TreeMenuFor(leaf);

        Headers(menu).Should().Equal("Promote to Production", "Move to", "Rename…", "Clone…", "Create Variant…", "Delete");
        menu.Single(i => i.Header == "Rename…").Gesture.Should().Be("F2");
        menu.Single(i => i.Header == "Delete").Gesture.Should().Be("Del");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        moveTo.Select(i => i.Header).Should().Equal("Production", "Research and Development");
        moveTo[0].Items.Select(i => i.Header).Should().Equal("Breads", "Cakes", "Uncategorized");
        moveTo[0].Items.Should().OnlyContain(i => i.IsEnabled);
        moveTo[1].Items.Single(i => i.Header == "Breads").IsEnabled.Should().BeFalse("it's in that folder already");

        await RunAsync(menu[0]);

        viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId))[0].Header
            .Should().Be("Send Back to Research and Development", "in Production it can only go back");
    }

    [Fact]
    public async Task Promote_saves_the_move_at_once_says_so_and_Undo_on_the_notice_puts_it_back()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Promote to Production"));

        viewModel.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, breadsId), "it keeps its category");
        TreeFolder(viewModel, ProductStage.Production, "Breads").Products.Should().ContainSingle();
        viewModel.MoveNotice.Should().Be("Moved Mamon to Production › Breads");

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.ResearchAndDevelopment, breadsId));
        TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Breads").Products.Should().ContainSingle();
        viewModel.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        (await viewModel.UndoMoveCommand.CanExecute.FirstAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Several_products_picked_move_together_and_the_one_showing_stays_on_the_form()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var cakesId = await AddProductCategoryAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var sansRivalId = await SaveProductAsync("Sans Rival", cakesId, ProductStage.Production);
        var viewModel = await OpenedViewModelAsync(mamonId);
        var tree = viewModel.ProductTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        viewModel.SelectedTreeNodes.Add(tree.Find(pandesalId)!);
        viewModel.SelectedTreeNodes.Add(tree.Find(sansRivalId)!);
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
        viewModel.SelectedTreeNode.Should().BeSameAs(tree.Find(mamonId));

        var menu = viewModel.TreeMenuFor(tree.Find(pandesalId));

        Headers(menu).Should().Equal("Promote 2 Products to Production", "Send 1 Product Back to Research and Development", "Move 3 Products to");
        await RunAsync(menu[0]);

        viewModel.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, breadsId));
        (await PlaceOfAsync(pandesalId)).Should().Be((ProductStage.Production, breadsId));
        (await PlaceOfAsync(sansRivalId)).Should().Be((ProductStage.Production, cakesId), "it was in Production already");
        viewModel.MoveNotice.Should().Be("Moved 2 products to Production › Breads");
        viewModel.SelectedTreeProducts.Select(l => l.Product.Id).Should().Equal(mamonId, pandesalId, sansRivalId);
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
    }

    [Fact]
    public async Task A_folders_menu_adds_to_it_promotes_what_it_shows_and_renames_or_deletes_its_category()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var testLoafId = await SaveProductAsync("Test Loaf", null);
        var viewModel = await LoadedViewModelAsync();

        var menu = viewModel.TreeMenuFor(TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Breads"));

        Headers(menu).Should().Equal("New Product Here", "Promote All to Production", "New Category…", "Rename Category…", "Delete Category");
        Headers(viewModel.TreeMenuFor(TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Uncategorized")))
            .Should().Equal("New Product Here", "Promote All to Production");
        Headers(viewModel.TreeMenuFor(TreeFolder(viewModel, ProductStage.Production, "Uncategorized")))
            .Should().Equal(["New Product Here"], "there's nothing in it to promote, and no category to rename");

        await RunAsync(menu.Single(i => i.Header == "Promote All to Production"));

        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, breadsId));
        (await PlaceOfAsync(pandesalId)).Should().Be((ProductStage.Production, breadsId));
        (await PlaceOfAsync(testLoafId)).Should().Be((ProductStage.ResearchAndDevelopment, (int?)null));
        viewModel.MoveNotice.Should().Be("Moved 2 products to Production › Breads");
    }

    [Fact]
    public async Task The_stage_and_list_menus_add_products_and_categories_and_open_or_close_every_folder()
    {
        await AddProductCategoryAsync("Breads");
        await AddProductCategoryAsync("Cakes");
        var viewModel = await LoadedViewModelAsync();
        var production = viewModel.ProductTree.StageFolder(ProductStage.Production);

        Headers(viewModel.TreeMenuFor(production)).Should().Equal("New Product Here", "New Category…", "Expand All", "Collapse All");
        Headers(viewModel.TreeMenuFor(null)).Should().Equal("New Product", "New Category…", "Expand All", "Collapse All");

        await RunAsync(viewModel.TreeMenuFor(production).Single(i => i.Header == "Expand All"));
        production.Children.Cast<ProductFolderNode>().Should().OnlyContain(f => f.IsExpanded);
        viewModel.ProductTree.StageFolder(ProductStage.ResearchAndDevelopment).Children.Cast<ProductFolderNode>()
            .Should().OnlyContain(f => !f.IsExpanded, "the other stage's folders are left as they were");

        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "Expand All"));
        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "Collapse All"));
        viewModel.ProductTree.Roots.SelectMany(r => r.Children).Cast<ProductFolderNode>().Should().OnlyContain(f => !f.IsExpanded);
        viewModel.ProductTree.Roots.Should().OnlyContain(r => r.IsExpanded);
    }

    [Fact]
    public async Task New_Product_Here_starts_a_product_in_the_folders_category_that_Save_files_in_its_stage()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var viewModel = await LoadedViewModelAsync();
        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        var breads = TreeFolder(viewModel, ProductStage.Production, "Breads");
        viewModel.NewProductTag.Should().Be("Goes in Research and Development", "the blank form is a new product too");

        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));

        viewModel.SelectedProduct.Should().BeNull();
        viewModel.EditingCategory!.Id.Should().Be(breadsId);
        viewModel.NewProductTag.Should().Be("Goes in Production", "the header says where Save will file it");
        viewModel.History.IsDirty.Should().BeFalse("nothing is typed yet");

        // Revert keeps the folder it was started from.
        TypeName(viewModel, "Something Else");
        await viewModel.RevertCommand.Execute();
        viewModel.EditingName.Should().BeEmpty();
        viewModel.EditingCategory!.Id.Should().Be(breadsId);
        viewModel.NewProductTag.Should().Be("Goes in Production");

        TypeName(viewModel, "Pan de Coco");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var saved = await ProductNamedAsync("Pan de Coco");
        (saved.Stage, saved.CategoryId).Should().Be((ProductStage.Production, breadsId));
        breads.Products.Select(l => l.Title).Should().Equal("Pan de Coco");
        viewModel.NewProductTag.Should().BeNull("a saved product shows no tag");

        // New, from the header, still starts one in Research and Development.
        await viewModel.NewCommand.Execute();
        viewModel.NewProductTag.Should().Be("Goes in Research and Development");
        TypeName(viewModel, "Test Loaf");
        await viewModel.SaveCommand.Execute();

        var plain = await ProductNamedAsync("Test Loaf");
        (plain.Stage, plain.CategoryId).Should().Be((ProductStage.ResearchAndDevelopment, (int?)null));
    }

    [Fact]
    public async Task Rename_on_the_product_showing_saves_the_name_and_leaves_what_else_is_typed_to_save()
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var viewModel = await OpenedViewModelAsync(mamonId);
        TextPromptRequest? asked = null;
        viewModel.PromptForName.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput("Mamon Tostado");
        });
        TypeDescription(viewModel, "Toasted and sugared");

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Rename…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked.Should().Be(new TextPromptRequest("Rename Product", "Name", "Mamon"));
        var saved = await ProductNamedAsync("Mamon Tostado");
        saved.Description.Should().BeNull("the rename saves the name alone");
        viewModel.EditingName.Should().Be("Mamon Tostado");
        viewModel.EditingCloneName.Should().Be("Mamon Tostado Copy", "the clone's name was still the one it started with");
        viewModel.ProductTree.Find(mamonId)!.Title.Should().Be("Mamon Tostado");
        viewModel.History.IsDirty.Should().BeTrue("the description is still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingDescription.Should().BeEmpty();
        viewModel.EditingName.Should().Be("Mamon Tostado", "no undo takes back a rename that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Clone_and_Create_Variant_ask_for_the_new_products_name_and_then_show_it()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId, ProductStage.Production);
        var viewModel = await LoadedViewModelAsync();
        var answer = (string?)"Mamon Mini";
        TextPromptRequest? asked = null;
        viewModel.PromptForName.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(answer);
        });

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Clone…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked!.Title.Should().Be("Clone Product");
        asked.InitialValue.Should().Be("Mamon Copy");
        viewModel.SelectedProduct!.Name.Should().Be("Mamon Mini");
        viewModel.SelectedProduct.Stage.Should().Be(ProductStage.ResearchAndDevelopment);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(viewModel.SelectedProduct.Id));

        answer = null;
        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Create Variant…"));

        asked.Title.Should().Be("Create Variant");
        viewModel.Products.Should().HaveCount(2, "a cancelled prompt makes nothing");
    }

    [Fact]
    public async Task New_Category_adds_a_folder_to_both_stages_and_picks_it_where_it_was_asked()
    {
        await AddProductCategoryAsync("Breads");
        var viewModel = await LoadedViewModelAsync();
        var answer = "Pastries";
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput(answer));

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, ProductStage.Production, "Breads")).Single(i => i.Header == "New Category…"));

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Categories.Select(c => c.Name).Should().Equal("Breads", "Pastries");
        TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Pastries").Should().NotBeNull();
        viewModel.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, ProductStage.Production, "Pastries"));

        answer = "breads";
        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.StageFolder(ProductStage.ResearchAndDevelopment)).Single(i => i.Header == "New Category…"));

        viewModel.Categories.Should().HaveCount(2, "a name that's a category already adds none");
        viewModel.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Breads"));
    }

    [Fact]
    public async Task Rename_Category_renames_its_folder_in_both_stages_and_the_picker_follows()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        TextPromptRequest? asked = null;
        viewModel.PromptForName.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput("Bread");
        });

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, ProductStage.Production, "Breads")).Single(i => i.Header == "Rename Category…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked!.Message.Should().Contain("renamed in both");
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.ProductCategories.SingleAsync(Cancellation)).Name.Should().Be("Bread");
        }

        TreeFolder(viewModel, ProductStage.Production, "Bread").Should().NotBeNull();
        TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Bread").Products.Should().ContainSingle();
        viewModel.EditingCategory!.Name.Should().Be("Bread");
        viewModel.History.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Category_asks_first_then_leaves_its_products_uncategorized_with_nothing_on_the_form_to_save()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        await AddProductCategoryAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        await SaveProductAsync("Pandesal", breadsId, ProductStage.Production);
        var viewModel = await OpenedViewModelAsync(mamonId);
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Breads")).Single(i => i.Header == "Delete Category"));

        viewModel.ErrorMessage.Should().BeNull();
        question.Should().Contain("'Breads'").And.Contain("its 2 products are left uncategorized");
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.ProductCategories.Select(c => c.Name).ToListAsync(Cancellation)).Should().Equal("Cakes");
            (await dbContext.Products.Select(p => p.CategoryId).ToListAsync(Cancellation)).Should().OnlyContain(id => id == null);
        }

        TitlesIn(viewModel.ProductTree.StageFolder(ProductStage.ResearchAndDevelopment)).Should().Equal("Cakes", "Uncategorized");
        TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Uncategorized").Products.Single().Product.Id.Should().Be(mamonId);
        viewModel.EditingCategory.Should().BeNull();
        viewModel.History.IsDirty.Should().BeFalse("the category went in the database: there's nothing to save");
    }

    [Fact]
    public async Task Delete_on_a_product_asks_first_and_the_form_starts_afresh_only_if_it_was_showing_it()
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var ensaymadaId = await SaveProductAsync("Ensaymada", null);
        var viewModel = await OpenedViewModelAsync(mamonId);
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(ensaymadaId)).Single(i => i.Header == "Delete"));

        question.Should().StartWith("Delete 'Ensaymada'?");
        viewModel.Products.Select(p => p.Name).Should().Equal("Mamon");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId, "the one showing wasn't the one deleted");

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Delete"));

        viewModel.Products.Should().BeEmpty();
        viewModel.SelectedProduct.Should().BeNull();
        viewModel.EditingName.Should().BeEmpty();
    }

    [Fact]
    public async Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var viewModel = await LoadedViewModelAsync();
        var mamon = viewModel.ProductTree.Find(mamonId)!;
        var breads = TreeFolder(viewModel, ProductStage.ResearchAndDevelopment, "Breads");

        viewModel.SelectedTreeNode = mamon;
        viewModel.TreeShortcut("F2").Should().Match<ProductTreeMenuItem>(i => i.Command == viewModel.RenameProductCommand && i.Parameter == mamon);
        viewModel.TreeShortcut("Del").Should().Match<ProductTreeMenuItem>(i => i.Command == viewModel.DeleteProductCommand && i.Parameter == mamon);

        viewModel.SelectedTreeNode = breads;
        viewModel.TreeShortcut("F2")!.Command.Should().BeSameAs(viewModel.RenameCategoryCommand);
        viewModel.TreeShortcut("Del")!.Command.Should().BeSameAs(viewModel.DeleteCategoryCommand);

        viewModel.SelectedTreeNode = viewModel.ProductTree.StageFolder(ProductStage.Production);
        viewModel.TreeShortcut("F2").Should().BeNull("a stage can't be renamed");
        viewModel.SelectedTreeNode = TreeFolder(viewModel, ProductStage.Production, "Uncategorized");
        viewModel.TreeShortcut("Del").Should().BeNull("nor Uncategorized deleted");

        viewModel.SelectedTreeNode = mamon;
        viewModel.SelectedTreeNodes.Add(viewModel.ProductTree.Find(pandesalId)!);
        viewModel.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    }

    [Fact]
    public async Task A_product_opened_from_elsewhere_is_picked_alone_in_the_list()
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        var ensaymadaId = await SaveProductAsync("Ensaymada", null);
        var viewModel = await OpenedViewModelAsync(mamonId);
        viewModel.SelectedTreeNodes.Add(viewModel.ProductTree.Find(pandesalId)!);

        viewModel.OpenProduct(ensaymadaId);

        viewModel.SelectedTreeNodes.Should().Equal(viewModel.ProductTree.Find(ensaymadaId)!);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(ensaymadaId));
    }

    [Fact]
    public async Task Undoing_a_move_back_into_a_category_deleted_since_is_refused_and_says_why()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var cakesId = await AddProductCategoryAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await LoadedViewModelAsync();
        var moveTo = viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Move to");
        await RunAsync(moveTo.Items.Single(i => i.Header == "Research and Development").Items.Single(i => i.Header == "Cakes"));
        await new ProductCategoryRepository(CreateDbContextFactory()).DeleteAsync(breadsId, Cancellation);

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("The category to move it to no longer exists.");
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.ResearchAndDevelopment, cakesId));
    }

    [Fact]
    public async Task The_folders_left_open_open_again_next_time()
    {
        var breadsId = await AddProductCategoryAsync("Breads");
        var cakesId = await AddProductCategoryAsync("Cakes");
        var store = new InMemoryViewStateStore();
        (await LoadedViewModelAsync(store)).Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedViewModelAsync(store);

        TreeFolder(first, ProductStage.Production, "Breads").IsExpanded = true;

        await WaitUntilSettledAsync(
            () => store.ReadList(ProductsViewModel.OpenFoldersKey)?.Contains($"Production/{breadsId}") == true,
            () => string.Join(", ", store.ReadList(ProductsViewModel.OpenFoldersKey) ?? []));

        var next = await LoadedViewModelAsync(store);
        TreeFolder(next, ProductStage.Production, "Breads").IsExpanded.Should().BeTrue();
        TreeFolder(next, ProductStage.ResearchAndDevelopment, "Breads").IsExpanded.Should().BeFalse();

        // One opened just before the app closes is written as it closes.
        TreeFolder(next, ProductStage.Production, "Cakes").IsExpanded = true;
        next.Dispose();
        store.ReadList(ProductsViewModel.OpenFoldersKey).Should().Contain($"Production/{cakesId}");
    }

    private static IEnumerable<string> Headers(IEnumerable<ProductTreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    private static IEnumerable<string> TitlesIn(ProductFolderNode folder) => folder.Children.Select(c => c.Title);

    private static ProductFolderNode TreeFolder(ProductsViewModel viewModel, ProductStage stage, string title) =>
        viewModel.ProductTree.StageFolder(stage).Children.OfType<ProductFolderNode>().Single(f => f.Title == title);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    private static async Task RunAsync(ProductTreeMenuItem item)
    {
        switch (item.Command)
        {
            case ReactiveCommand<IReadOnlyList<ProductMove>, RxVoid> move:
                await move.Execute((IReadOnlyList<ProductMove>)item.Parameter!);
                break;
            case ReactiveCommand<ProductLeafNode, RxVoid> onProduct:
                await onProduct.Execute((ProductLeafNode)item.Parameter!);
                break;
            case ReactiveCommand<ProductFolderNode, RxVoid> onFolder:
                await onFolder.Execute((ProductFolderNode?)item.Parameter!);
                break;
            default:
                throw new InvalidOperationException($"'{item.Header}' runs nothing a test knows how to wait for.");
        }
    }

    /// <summary>A name typed into the header and left, as one undo step, the way the name box records it.</summary>
    private static void TypeName(ProductsViewModel viewModel, string name)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        viewModel.EditingName = name;
        recorder.EndUndoStep();
    }

    private static void TypeDescription(ProductsViewModel viewModel, string description)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        viewModel.EditingDescription = description;
        recorder.EndUndoStep();
    }

    private async Task<int> AddProductCategoryAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        var category = new ProductCategory { Name = name };
        dbContext.ProductCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    /// <summary>A product with nothing but its name, category and stage, saved as the Products screen saves a new one.</summary>
    private Task<int> SaveProductAsync(string name, int? categoryId, ProductStage stage = ProductStage.ResearchAndDevelopment) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, CategoryId = categoryId, Stage = stage }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private async Task<(ProductStage Stage, int? CategoryId)> PlaceOfAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        var product = await dbContext.Products.SingleAsync(p => p.Id == productId, Cancellation);
        return (product.Stage, product.CategoryId);
    }

    private async Task<Product> ProductNamedAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Products.SingleAsync(p => p.Name == name, Cancellation);
    }

    private async Task<ProductsViewModel> LoadedViewModelAsync(IViewStateStore? viewStateStore = null)
    {
        var viewModel = CreateViewModel(viewStateStore);
        await viewModel.LoadCommand.Execute();
        return viewModel;
    }

    /// <summary>The screen with <paramref name="productId"/> open on the form, its details in.</summary>
    private async Task<ProductsViewModel> OpenedViewModelAsync(int productId)
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.OpenProduct(productId);
        await WaitUntilSettledAsync(() => viewModel.AreDetailsReady && viewModel.SelectedProduct?.Id == productId, () => $"ready={viewModel.AreDetailsReady}");
        return viewModel;
    }

    private ProductsViewModel CreateViewModel(IViewStateStore? viewStateStore)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();
        var packagingLineRepository = new PackagingLineRepository(factory);

        return new ProductsViewModel(
            new ProductRepository(factory),
            new ProductSubAssemblyUsageRepository(factory),
            packagingLineRepository,
            new CostingParametersRepository(factory),
            new CostingDefaultsRepository(factory),
            new PackagingLinkRepository(factory, packagingLineRepository),
            new ProductCloner(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory, CreateCostPropagator()),
            new ProductCategoryRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            viewStateStore ?? new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> has held for a moment: a product's detail loads in the background, and
    /// the open folders are written once they settle, with nothing to await.
    /// </summary>
    private static async Task WaitUntilSettledAsync(Func<bool> condition, Func<string> describe)
    {
        var deadline = Stopwatch.StartNew();
        var heldFor = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(15))
        {
            if (!condition())
            {
                heldFor.Restart();
            }
            else if (heldFor.Elapsed > TimeSpan.FromMilliseconds(500))
            {
                return;
            }

            await Task.Delay(25);
        }

        throw new TimeoutException($"It never settled: {describe()}.");
    }
}
