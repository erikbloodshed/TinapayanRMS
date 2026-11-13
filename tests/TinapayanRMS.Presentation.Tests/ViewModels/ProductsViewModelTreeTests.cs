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
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// The Products list's right-click menus, its keys, its selection and its category filter: what each menu offers a
/// product, several products, a folder and the list itself, and what each item does, run as the menu runs it. Moves,
/// renames and folders save at once; the notice of a move can take it back; the folders left open open again next time.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class ProductsViewModelTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_products_menu_moves_it_to_the_other_stage_or_any_folder_and_offers_what_else_can_be_done_with_it() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        await AddFolderAsync("Cakes");
        var bakeryId = await AddFolderAsync("Bakery", ProductStage.Production);
        await AddFolderAsync("Rolls", ProductStage.Production, bakeryId);
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await LoadedViewModelAsync();
        var leaf = viewModel.ProductTree.Find(mamonId)!;
        viewModel.SelectedTreeNode = leaf;

        var menu = viewModel.TreeMenuFor(leaf);

        Headers(menu).Should().Equal("Promote to Production", "Move to", "Cut", "Paste", "Apply Costing Defaults…", "Rename", "Clone…", "Create Variant…", "Delete");
        menu.Single(i => i.Header == "Cut").Gesture.Should().Be("Ctrl+X");
        menu.Single(i => i.Header == "Paste").Should().Match<TreeMenuItem>(i => i.Gesture == "Ctrl+V" && !i.IsEnabled, "nothing is cut yet");
        menu.Single(i => i.Header == "Rename").Gesture.Should().Be("F2");
        menu.Single(i => i.Header == "Delete").Gesture.Should().Be("Del");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        moveTo.Select(i => i.Header).Should().Equal("Production", "Innovation");
        Headers(moveTo[0].Items).Should().Equal("Production", "Bakery");
        Headers(moveTo[0].Items.Single(i => i.Header == "Bakery").Items).Should().Equal(["Bakery", "Rolls"], "a folder with folders in it is a submenu, itself first");
        Headers(moveTo[1].Items).Should().Equal("Innovation", "Breads", "Cakes");
        moveTo[1].Items.Single(i => i.Header == "Breads").IsEnabled.Should().BeFalse("it's in that folder already");
        moveTo[1].Items.Single(i => i.Header == "Innovation").IsEnabled.Should().BeTrue("the top of the stage is a place too");

        await RunAsync(moveTo[0].Items.Single(i => i.Header == "Bakery").Items.Single(i => i.Header == "Rolls"));
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, await FolderIdAsync("Rolls")));
        viewModel.MoveNotice.Should().Be("Moved Mamon to Production › Bakery › Rolls");

        viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId))[0].Header
            .Should().Be("Send Back to Innovation", "in Production it can only go back");
    });

    [Test]
    public Task A_drop_moves_products_or_one_folder_onto_a_folder_and_gives_the_first_back_to_bring_into_view() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await LoadedViewModelAsync();
        IListTreeScreen screen = viewModel;
        var mamon = viewModel.ProductTree.Find(mamonId)!;
        var breads = viewModel.ProductTree.Folder(breadsId)!;
        var cakes = viewModel.ProductTree.Folder(cakesId)!;
        var production = viewModel.ProductTree.StageFolder(ProductStage.Production);

        screen.CanDrag([mamon]).Should().BeTrue();
        screen.CanDrag([cakes]).Should().BeTrue("one folder is dragged with all it holds");
        screen.CanDrag([mamon, cakes]).Should().BeFalse("not a folder and products together");
        screen.CanDrag([production]).Should().BeFalse("a stage is fixed");
        screen.CanDrop([mamon], breads).Should().BeFalse("it's there already");
        screen.CanDrop([mamon], viewModel.ProductTree.DeletedFolder).Should().BeFalse("nothing is dropped into Deleted");
        screen.CanDrop([mamon], cakes).Should().BeTrue();

        (await screen.Drop([mamon], cakes)).Should().BeSameAs(mamon, "its row keeps its node, in its new folder");

        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, cakesId));
        cakes.Products.Should().ContainSingle();
        viewModel.MoveNotice.Should().Be("Moved Mamon to Innovation › Cakes");
        (await screen.Drop([mamon], cakes)).Should().BeNull("a drop that moves nothing has no row to show");

        // A folder dropped in the other stage takes what it holds there.
        screen.CanDrop([cakes], cakes).Should().BeFalse("a folder doesn't go inside itself");
        (await screen.Drop([cakes], production)).Should().BeSameAs(cakes);

        viewModel.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, cakesId), "a product goes with its folder");
        viewModel.MoveNotice.Should().Be("Moved Cakes to Production");
        production.Folders.Should().Contain(cakes);
        cakes.Stage.Should().Be(ProductStage.Production);

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, cakesId));
        viewModel.ProductTree.StageFolder(ProductStage.Innovation).Folders.Should().Contain(cakes, "Undo puts the folder back with it");
    });

    [Test]
    public Task Promote_files_it_at_the_same_path_in_Production_and_Undo_takes_away_the_folders_it_made() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", ProductStage.Innovation, breadsId);
        var productionBreadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", holidayId);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Promote to Production"));

        viewModel.ErrorMessage.Should().BeNull();
        var (stage, folderId) = await PlaceOfAsync(mamonId);
        stage.Should().Be(ProductStage.Production);
        var made = viewModel.ProductTree.Folder(folderId!.Value)!;
        made.Title.Should().Be("Holiday", "Holiday was made in Production's Breads, which was there already");
        made.ParentFolderId.Should().Be(productionBreadsId);
        made.Products.Should().ContainSingle();
        viewModel.MoveNotice.Should().Be("Moved Mamon to Production › Breads › Holiday");

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, holidayId));
        viewModel.ProductTree.Folder(holidayId)!.Products.Should().ContainSingle();
        viewModel.ProductTree.Folder(made.FolderId!.Value).Should().BeNull("the folder the Promote made goes, empty again");
        viewModel.ProductTree.Folder(productionBreadsId).Should().NotBeNull("one it found there stays");
        viewModel.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        (await viewModel.UndoMoveCommand.CanExecute.FirstAsync()).Should().BeFalse();
    });

    [Test]
    public Task A_Promote_counts_only_what_the_database_moved_when_another_computer_moved_one_first() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedTreeNode = viewModel.ProductTree.Find(mamonId);
        viewModel.SelectedTreeNodes.Add(viewModel.ProductTree.Find(pandesalId)!);
        var promote = viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Promote 2 Products to Production");

        // Promoted elsewhere while this list still shows them in Innovation.
        await new ProductRepository(CreateDbContextFactory()).MoveToStageAsync([pandesalId], ProductStage.Production, Cancellation);
        await RunAsync(promote);

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.MoveNotice.Should().Be("Moved Mamon to Production › Breads");
        await viewModel.UndoMoveCommand.Execute();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId));
        (await PlaceOfAsync(pandesalId)).Stage.Should().Be(ProductStage.Production, "Undo puts back only what this Promote moved");

        // With nothing left to move, nothing is said and there's nothing to undo.
        var again = viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header.StartsWith("Promote", StringComparison.Ordinal));
        await new ProductRepository(CreateDbContextFactory()).MoveToStageAsync([mamonId], ProductStage.Production, Cancellation);
        await RunAsync(again);

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.MoveNotice.Should().BeNull();
    });

    [Test]
    public Task Cut_marks_the_products_picked_and_Paste_on_a_folder_moves_them_there_with_Undo() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var ensaymadaId = await SaveProductAsync("Ensaymada", breadsId);
        var viewModel = await LoadedViewModelAsync();
        var tree = viewModel.ProductTree;
        var cakes = tree.Folder(cakesId)!;
        viewModel.SelectedTreeNode = tree.Find(mamonId);
        viewModel.SelectedTreeNodes.Add(tree.Find(pandesalId)!);

        // Ctrl+X with both picked: the menu of the first, which covers both.
        await RunAsync(viewModel.TreeShortcut("Ctrl+X")!);

        viewModel.CutNodes.Should().Equal(tree.Find(mamonId)!, tree.Find(pandesalId)!);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId), "nothing moves until the paste");
        viewModel.TreeMenuFor(tree.Find(ensaymadaId)).Single(i => i.Header == "Paste").IsEnabled.Should().BeFalse("they're in that folder already");
        var paste = viewModel.TreeMenuFor(cakes).Single(i => i.Header == "Paste");
        paste.IsEnabled.Should().BeTrue();

        // Ctrl+V with the folder picked: what its menu's Paste does.
        viewModel.SelectedTreeNode = cakes;
        viewModel.TreeShortcut("Ctrl+V").Should().Match<TreeMenuItem>(i => i.Command == viewModel.PasteCommand && i.Parameter == cakes);
        await RunAsync(paste);

        viewModel.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, cakesId));
        (await PlaceOfAsync(pandesalId)).Should().Be((ProductStage.Production, cakesId));
        (await PlaceOfAsync(ensaymadaId)).Should().Be((ProductStage.Innovation, breadsId));
        viewModel.CutNodes.Should().BeEmpty("a paste uses up the cut");
        viewModel.MoveNotice.Should().Be("Moved 2 products to Production › Cakes");
        viewModel.TreeMenuFor(cakes).Single(i => i.Header == "Paste").IsEnabled.Should().BeFalse();

        await viewModel.UndoMoveCommand.Execute();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId));

        // A folder is cut with all it holds, alone; Esc forgets a cut.
        var breads = tree.Folder(breadsId)!;
        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "Cut"));
        viewModel.CutNodes.Should().Equal(breads);
        viewModel.TreeMenuFor(tree.Find(mamonId)).Single(i => i.Header == "Paste").IsEnabled.Should().BeFalse("not inside itself");
        viewModel.SelectedTreeNode = breads;
        await RunAsync(viewModel.TreeShortcut("Esc")!);
        viewModel.CutNodes.Should().BeEmpty();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId));

        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "Cut"));
        await RunAsync(viewModel.TreeMenuFor(cakes).Single(i => i.Header == "Paste"));
        viewModel.ErrorMessage.Should().BeNull();
        breads.ParentFolder.Should().BeSameAs(cakes);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, breadsId), "its products go with it, to the other stage");
        viewModel.TreeShortcut("Esc").Should().BeNull("there's nothing cut to forget");
    });

    [Test]
    public Task Several_products_picked_move_together_and_the_one_showing_stays_on_the_form() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
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

        Headers(menu).Should().Equal(
            "Promote 2 Products to Production", "Send 1 Product Back to Innovation", "Move 3 Products to", "Cut 3 Products", "Paste",
            "Apply Costing Defaults to 3 Products…");
        await RunAsync(menu[0]);

        viewModel.ErrorMessage.Should().BeNull();
        var productionBreadsId = await FolderIdAsync("Breads", ProductStage.Production);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, productionBreadsId));
        (await PlaceOfAsync(pandesalId)).Should().Be(((ProductStage, int?))(ProductStage.Production, productionBreadsId), "one folder is made for both");
        (await PlaceOfAsync(sansRivalId)).Should().Be((ProductStage.Production, cakesId), "it was in Production already");
        viewModel.MoveNotice.Should().Be("Moved 2 products to Production › Breads");
        viewModel.SelectedTreeProducts.Select(l => l.Product.Id).Should().Equal(mamonId, pandesalId, sansRivalId);
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
    });

    [Test]
    public Task Apply_Costing_Defaults_sets_the_products_picked_to_them_once_confirmed_and_the_one_showing_follows() => UiThread.RunAsync(async () =>
    {
        await new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(
            new CostingDefaults { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarginPercent = 60m }, Cancellation);
        var mamonId = await SaveProductAsync("Mamon", null);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        var viewModel = await OpenedViewModelAsync(mamonId);
        string? asked = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(true);
        });
        viewModel.SelectedTreeNodes.Add(viewModel.ProductTree.Find(pandesalId)!);

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(pandesalId)).Single(i => i.Header == "Apply Costing Defaults to 2 Products…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked.Should().StartWith("Set the costing of Mamon and Pandesal to the costing defaults: labor 35%, overhead 25%, administrative 15% and markup 60%?");
        viewModel.SuccessMessage.Should().Be("The costing defaults were applied to Mamon and Pandesal, with a version of each.");
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.Costing.EditingMarginPercent == 60m,
            () => $"ready={viewModel.AreDetailsReady}, markup={viewModel.Costing.EditingMarginPercent}");
        viewModel.Costing.EditingLaborPercent.Should().Be(35m, "the product showing reads its new costing");
        viewModel.HasUnsavedChanges.Should().BeFalse("its new costing is how it's saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.CostingParameters.Where(c => c.ProductId == pandesalId).Select(c => c.MarginPercent).SingleAsync(Cancellation)).Should().Be(60m);
    });

    [Test]
    public Task A_folders_menu_adds_to_it_promotes_all_it_holds_moves_renames_or_deletes_it() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", ProductStage.Innovation, breadsId);
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var bibingkaId = await SaveProductAsync("Bibingka", holidayId);
        var testLoafId = await SaveProductAsync("Test Loaf", null);
        var viewModel = await LoadedViewModelAsync();
        var breads = viewModel.ProductTree.Folder(breadsId)!;

        var menu = viewModel.TreeMenuFor(breads);

        Headers(menu).Should().Equal(
            "New Product Here", "New Composite Product Here", "New Folder", "Promote All to Production", "Move to", "Cut", "Paste", "Print Folder",
            "Rename", "Delete", "Expand All", "Collapse All");
        Headers(viewModel.TreeMenuFor(viewModel.ProductTree.Folder(cakesId)))
            .Should().NotContain("Promote All to Production", "there's nothing in it to promote");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        Headers(moveTo.Single(i => i.Header == "Innovation").Items).Should().Equal(["Innovation"], "not itself, nor the folders in it");
        Headers(moveTo.Single(i => i.Header == "Production").Items).Should().Equal("Production", "Cakes");

        await RunAsync(menu.Single(i => i.Header == "Promote All to Production"));

        viewModel.ErrorMessage.Should().BeNull();
        var productionBreadsId = await FolderIdAsync("Breads", ProductStage.Production);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, productionBreadsId));
        (await PlaceOfAsync(bibingkaId)).Stage.Should().Be(ProductStage.Production, "the folders in it go too");
        viewModel.ProductTree.Find(bibingkaId)!.Should().Match<ProductLeafNode>(l => viewModel.ProductTree.Folder(l.Product.FolderId!.Value)!.Title == "Holiday");
        (await PlaceOfAsync(testLoafId)).Should().Be((ProductStage.Innovation, (int?)null));
        viewModel.MoveNotice.Should().Be("Moved 2 products to Production");
    });

    [Test]
    public Task The_stage_and_list_menus_add_products_and_folders_and_open_or_close_every_folder() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        await AddFolderAsync("Holiday", ProductStage.Production, breadsId);
        await AddFolderAsync("Trials");
        var viewModel = await LoadedViewModelAsync();
        var production = viewModel.ProductTree.StageFolder(ProductStage.Production);
        var innovation = viewModel.ProductTree.StageFolder(ProductStage.Innovation);

        Headers(viewModel.TreeMenuFor(production)).Should().Equal("New Product Here", "New Composite Product Here", "New Folder", "Paste", "Print Stage", "Expand All", "Collapse All");
        Headers(viewModel.TreeMenuFor(null)).Should().Equal("New Product", "New Composite Product", "New Folder", "Print All Products", "Expand All", "Collapse All");

        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "Collapse All"));
        viewModel.Folders.Select(f => viewModel.ProductTree.Folder(f.Id)!).Should().OnlyContain(f => !f.IsExpanded);
        viewModel.ProductTree.Stages.Should().OnlyContain(r => r.IsExpanded);

        await RunAsync(viewModel.TreeMenuFor(production).Single(i => i.Header == "Expand All"));
        viewModel.Folders.Where(f => f.Stage == ProductStage.Production).Select(f => viewModel.ProductTree.Folder(f.Id)!).Should().OnlyContain(f => f.IsExpanded, "however deep");
        innovation.Folders.Should().OnlyContain(f => !f.IsExpanded, "the other stage's folders are left as they were");

        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "Expand All"));
        viewModel.Folders.Select(f => viewModel.ProductTree.Folder(f.Id)!).Should().OnlyContain(f => f.IsExpanded);
    });

    [Test]
    public Task New_Product_Here_starts_a_product_in_the_folder_that_Save_files_there_with_no_category() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var viewModel = await LoadedViewModelAsync();
        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        var breads = viewModel.ProductTree.Folder(breadsId)!;
        viewModel.NewProductTag.Should().Be("Goes in Production", "the blank form is a new product too, in Settings' stage");

        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));

        viewModel.SelectedProduct.Should().BeNull();
        viewModel.EditingCategory.Should().BeNull("a folder isn't a category");
        viewModel.NewProductTag.Should().Be("Goes in Innovation › Breads", "the header says where Save will file it");
        viewModel.History.IsDirty.Should().BeFalse("nothing is typed yet");

        // Revert keeps the folder it was started from.
        viewModel.CancelTreeName();
        TypeName(viewModel, "Something Else");
        await viewModel.RevertCommand.Execute();
        viewModel.EditingName.Should().BeEmpty();
        viewModel.NewProductTag.Should().Be("Goes in Innovation › Breads");

        TypeName(viewModel, "Pan de Coco");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        var saved = await ProductNamedAsync("Pan de Coco");
        (saved.Stage, saved.FolderId, saved.CategoryId).Should().Be((ProductStage.Innovation, breadsId, (int?)null));
        breads.Products.Select(l => l.Title).Should().Equal("Pan de Coco");
        viewModel.NewProductTag.Should().BeNull("a saved product shows no tag");

        // New, from the header, starts one in the stage Settings gives a new product: Production, to start with.
        await viewModel.NewCommand.Execute();
        viewModel.NewProductTag.Should().Be("Goes in Production");
        viewModel.CancelTreeName();
        TypeName(viewModel, "Test Loaf");
        await viewModel.SaveCommand.Execute();

        var plain = await ProductNamedAsync("Test Loaf");
        (plain.Stage, plain.FolderId).Should().Be((ProductStage.Production, (int?)null));
    });

    [Test]
    public Task New_files_a_product_in_the_stage_Settings_gives_a_new_one_and_follows_it_when_it_changes() => UiThread.RunAsync(async () =>
    {
        var defaults = new CostingDefaultsRepository(CreateDbContextFactory());
        await defaults.SetAsync(new CostingDefaults { NewProductStage = ProductStage.Innovation }, Cancellation);
        var viewModel = await LoadedViewModelAsync();
        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));

        viewModel.NewProductTag.Should().Be("Goes in Innovation");
        TypeName(viewModel, "Test Loaf");
        await viewModel.SaveCommand.Execute();
        viewModel.ErrorMessage.Should().BeNull();
        (await ProductNamedAsync("Test Loaf")).Stage.Should().Be(ProductStage.Innovation);

        // Changed in Settings while a new product is being typed: its stage was never picked, so it follows. Revert keeps it.
        await viewModel.NewCommand.Execute();
        TypeName(viewModel, "Pan de Coco");
        await defaults.SetAsync(new CostingDefaults { NewProductStage = ProductStage.Production }, Cancellation);
        await viewModel.LoadCommand.Execute();
        viewModel.NewProductTag.Should().Be("Goes in Production");
        viewModel.EditingName.Should().Be("Pan de Coco", "only the stage follows the setting");
        await viewModel.RevertCommand.Execute();
        viewModel.NewProductTag.Should().Be("Goes in Production");

        // A product started from a folder keeps that folder's stage whatever the setting says.
        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.StageFolder(ProductStage.Innovation)).Single(i => i.Header == "New Product Here"));
        await defaults.SetAsync(new CostingDefaults { NewProductStage = ProductStage.Production }, Cancellation);
        await viewModel.LoadCommand.Execute();
        viewModel.NewProductTag.Should().Be("Goes in Innovation");
    });

    [Test]
    public Task Rename_types_the_name_in_place_saves_it_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var viewModel = await OpenedViewModelAsync(mamonId);
        TypeDescription(viewModel, "Toasted and sugared");
        var mamon = viewModel.ProductTree.Find(mamonId)!;

        await RunAsync(viewModel.TreeMenuFor(mamon).Single(i => i.Header == "Rename"));

        viewModel.TreeNameEdit.Should().Be(new TreeNameEdit(mamon, "Mamon"));
        viewModel.SelectedTreeNode.Should().BeSameAs(mamon);

        await viewModel.CommitTreeNameAsync("  Mamon Tostado ");
        await viewModel.CommitTreeNameAsync("Mamon Again");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.TreeNameEdit.Should().BeNull();
        var saved = await ProductNamedAsync("Mamon Tostado");
        saved.Description.Should().BeNull("the rename saves the name alone");
        viewModel.Products.Should().NotContain(p => p.Name == "Mamon Again", "leaving the box after Enter saves nothing more");
        viewModel.EditingName.Should().Be("Mamon Tostado");
        viewModel.EditingCloneName.Should().Be("Mamon Tostado Copy", "the clone's name was still the one it started with");
        viewModel.ProductTree.Find(mamonId)!.Title.Should().Be("Mamon Tostado");
        viewModel.History.IsDirty.Should().BeTrue("the description is still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingDescription.Should().BeEmpty();
        viewModel.EditingName.Should().Be("Mamon Tostado", "no undo takes back a rename that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Esc_an_empty_name_the_same_name_or_a_search_leave_a_rename_unsaved() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var viewModel = await LoadedViewModelAsync();
        var mamon = viewModel.ProductTree.Find(mamonId)!;
        viewModel.SelectedTreeNode = mamon;

        // F2 does what the menu's Rename does.
        await RunAsync(viewModel.TreeShortcut("F2")!);
        viewModel.TreeNameEdit!.Node.Should().BeSameAs(mamon);
        viewModel.CancelTreeName();
        viewModel.TreeNameEdit.Should().BeNull();

        await RunAsync(viewModel.TreeShortcut("F2")!);
        await viewModel.CommitTreeNameAsync("   ");
        viewModel.TreeNameEdit.Should().BeNull("an empty name is an Esc");

        await RunAsync(viewModel.TreeShortcut("F2")!);
        await viewModel.CommitTreeNameAsync("Mamon");
        viewModel.TreeNameEdit.Should().BeNull();

        await RunAsync(viewModel.TreeShortcut("F2")!);
        viewModel.SearchText = "mam";
        viewModel.TreeNameEdit.Should().BeNull("a search may hide the row");

        viewModel.SearchText = string.Empty;
        await RunAsync(viewModel.TreeShortcut("F2")!);
        viewModel.CategoryFilter = new ProductCategoryFilter.Uncategorized();
        viewModel.TreeNameEdit.Should().BeNull("and so may the category filter");

        viewModel.ErrorMessage.Should().BeNull();
        (await NameOfAsync(mamonId)).Should().Be("Mamon");
    });

    [Test]
    public Task A_name_refused_shows_why_and_brings_the_box_back_with_it() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        await AddFolderAsync("Cakes");
        await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var viewModel = await LoadedViewModelAsync();

        // A new product named as another is.
        await viewModel.NewCommand.Execute();
        var row = viewModel.ProductTree.PendingProduct!;
        await viewModel.CommitTreeNameAsync("Mamon");

        viewModel.ErrorMessage.Should().NotBeNull();
        viewModel.TreeNameEdit.Should().Be(new TreeNameEdit(row, "Mamon"));
        viewModel.ProductTree.PendingProduct.Should().BeSameAs(row);
        viewModel.SelectedProduct.Should().BeNull();
        viewModel.CancelTreeName();
        viewModel.EditingName.Should().BeEmpty("Esc leaves the new product blank, as New does");
        viewModel.Products.Should().HaveCount(2);

        // A product renamed as another is.
        var pandesal = viewModel.ProductTree.Find(pandesalId)!;
        await RunAsync(viewModel.TreeMenuFor(pandesal).Single(i => i.Header == "Rename"));
        await viewModel.CommitTreeNameAsync("Mamon");

        viewModel.ErrorMessage.Should().NotBeNull();
        viewModel.TreeNameEdit.Should().Be(new TreeNameEdit(pandesal, "Mamon"));
        (await NameOfAsync(pandesalId)).Should().Be("Pandesal");
        viewModel.CancelTreeName();

        // A folder renamed as one beside it is, whatever the case.
        var breads = viewModel.ProductTree.Folder(breadsId)!;
        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "Rename"));
        await viewModel.CommitTreeNameAsync("cakes");

        viewModel.ErrorMessage.Should().Be("Innovation already has a folder named cakes.");
        viewModel.TreeNameEdit!.Node.Should().BeSameAs(breads);
        viewModel.TreeNameEdit.InitialText.Should().Be("cakes");
        viewModel.Folders.Select(f => f.Name).Should().Equal("Breads", "Cakes");
    });

    [Test]
    public Task Clone_and_Create_Variant_ask_for_the_new_products_name_and_then_show_it_in_Innovation_at_its_folders_path() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
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
        viewModel.SelectedProduct.Stage.Should().Be(ProductStage.Innovation);
        viewModel.SelectedProduct.FolderId.Should().Be(await FolderIdAsync("Breads", ProductStage.Innovation), "a Breads folder is made in Innovation for it");
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(viewModel.SelectedProduct.Id));

        answer = null;
        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Create Variant…"));

        asked.Title.Should().Be("Create Variant");
        viewModel.Products.Should().HaveCount(2, "a cancelled prompt makes nothing");
    });

    [Test]
    public Task New_Folder_names_a_folder_in_place_inside_the_one_it_was_asked_on_and_picks_it() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId, ProductStage.Production);
        var viewModel = await LoadedViewModelAsync();
        var breads = viewModel.ProductTree.Folder(breadsId)!;

        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "New Folder"));

        var folder = viewModel.ProductTree.PendingFolder!;
        TitlesIn(breads).Should().Equal("New folder", "Pandesal");
        viewModel.SelectedTreeNode.Should().BeSameAs(folder);
        viewModel.TreeNameEdit!.Node.Should().BeSameAs(folder);
        viewModel.TreeNameEdit.InitialText.Should().Be("New folder");
        viewModel.TreeNameEdit.Hint.Should().Contain("isn't a category");
        viewModel.TreeMenuFor(folder).Should().BeEmpty();

        await viewModel.LoadCommand.Execute();
        TitlesIn(breads).Should().Equal(["New folder", "Pandesal"], "a load keeps it");

        await viewModel.CommitTreeNameAsync("Holiday");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.ProductTree.PendingFolder.Should().BeNull();
        var holiday = await SingleFolderAsync("Holiday");
        (holiday.Stage, holiday.ParentId).Should().Be((ProductStage.Production, breadsId));
        TitlesIn(breads).Should().Equal("Holiday", "Pandesal");
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Folder(holiday.Id));
        viewModel.Categories.Should().BeEmpty("a folder adds no category");

        // From the list's empty space, in the folder of the product picked; a name taken there is refused.
        viewModel.SelectedTreeNode = viewModel.ProductTree.Find(pandesalId);
        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        viewModel.ProductTree.PendingFolder!.ParentFolderId.Should().Be(breadsId);
        await viewModel.CommitTreeNameAsync("holiday");

        viewModel.ErrorMessage.Should().Be("Production › Breads already has a folder named holiday.");
        viewModel.TreeNameEdit!.InitialText.Should().Be("holiday", "the box comes back with it");
        viewModel.ProductTree.PendingFolder!.ParentFolderId.Should().Be(breadsId);
        viewModel.CancelTreeName();

        // With a stage picked, at the top of that stage.
        viewModel.SelectedTreeNode = viewModel.ProductTree.StageFolder(ProductStage.Innovation);
        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        viewModel.ProductTree.PendingFolder!.Should().Match<ProductFolderNode>(f => f.Stage == ProductStage.Innovation && f.ParentFolderId == null);
    });

    [Test]
    public Task Rename_on_a_folder_types_the_name_in_place_and_renames_it_with_nothing_on_the_form_to_save() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        var breads = viewModel.ProductTree.Folder(breadsId)!;
        viewModel.SelectedTreeNode = breads;

        await RunAsync(viewModel.TreeShortcut("F2")!);

        viewModel.TreeNameEdit!.Node.Should().BeSameAs(breads);
        viewModel.TreeNameEdit.InitialText.Should().Be("Breads");

        await viewModel.CommitTreeNameAsync("Bread");

        viewModel.ErrorMessage.Should().BeNull();
        (await SingleFolderAsync("Bread")).Id.Should().Be(breadsId);
        viewModel.ProductTree.Folder(breadsId).Should().BeSameAs(breads);
        breads.Title.Should().Be("Bread");
        breads.Products.Should().ContainSingle();
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task New_Product_Here_names_the_product_in_its_folder_and_saves_it_there_at_once() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var viewModel = await LoadedViewModelAsync();
        var breads = viewModel.ProductTree.Folder(breadsId)!;
        viewModel.SearchText = "nothing like it";

        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));

        viewModel.SearchText.Should().BeEmpty("the search is cleared, so the new row shows");
        var row = viewModel.ProductTree.PendingProduct!;
        breads.Children.Should().Equal(row);
        viewModel.SelectedTreeNode.Should().BeSameAs(row);
        viewModel.TreeNameEdit.Should().Be(new TreeNameEdit(row, "New product"));
        viewModel.TreeMenuFor(row).Should().BeEmpty();
        viewModel.SelectedTreeProducts.Should().BeEmpty();
        viewModel.SelectedProduct.Should().BeNull();
        viewModel.History.IsDirty.Should().BeFalse();

        await viewModel.LoadCommand.Execute();
        breads.Children.Should().Equal([row], "a load keeps it");
        viewModel.SelectedTreeNode.Should().BeSameAs(row);

        await viewModel.CommitTreeNameAsync("Pan de Coco");
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady, () => $"ready={viewModel.AreDetailsReady}");

        viewModel.ErrorMessage.Should().BeNull();
        var saved = await ProductNamedAsync("Pan de Coco");
        (saved.Stage, saved.FolderId, saved.IsComposite).Should().Be((ProductStage.Innovation, breadsId, false));
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.ProductVersions.CountAsync(v => v.ProductId == saved.Id, Cancellation)).Should().Be(1, "it's saved as Save saves it");
        }

        viewModel.SelectedProduct!.Id.Should().Be(saved.Id, "the form opens on it");
        viewModel.ProductTree.PendingProduct.Should().BeNull();
        viewModel.TreeNameEdit.Should().BeNull();
        breads.Children.Should().Equal(viewModel.ProductTree.Find(saved.Id)!);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(saved.Id));
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_new_composite_named_in_the_list_keeps_its_row_and_the_name_on_the_form_until_it_can_be_saved() => UiThread.RunAsync(async () =>
    {
        var viewModel = await LoadedViewModelAsync();
        var production = viewModel.ProductTree.StageFolder(ProductStage.Production);

        await viewModel.NewCompositeCommand.Execute();

        var row = viewModel.ProductTree.PendingProduct!;
        production.Children.Should().Equal(row);
        row.Product.IsComposite.Should().BeTrue();
        viewModel.IsEditingComposite.Should().BeTrue();

        await viewModel.CommitTreeNameAsync("Pasalubong Box");

        // It needs two products before it can be saved: the name is the form's, unsaved, and the row keeps it.
        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SuccessMessage.Should().Contain("Products table");
        viewModel.Products.Should().BeEmpty();
        viewModel.TreeNameEdit.Should().BeNull();
        viewModel.ProductTree.PendingProduct.Should().BeSameAs(row);
        row.Title.Should().Be("Pasalubong Box");
        viewModel.SelectedTreeNode.Should().BeSameAs(row);
        viewModel.EditingName.Should().Be("Pasalubong Box");
        viewModel.History.IsDirty.Should().BeTrue("Save is offered for it");

        viewModel.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        await viewModel.RevertCommand.Execute();

        viewModel.ProductTree.PendingProduct.Should().BeNull("put back blank, it has no row");
        production.Children.Should().BeEmpty();
    });

    [Test]
    public Task Esc_or_an_empty_name_takes_a_new_row_away_and_saves_nothing() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var viewModel = await LoadedViewModelAsync();
        var breads = viewModel.ProductTree.Folder(breadsId)!;
        var innovation = viewModel.ProductTree.StageFolder(ProductStage.Innovation);

        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));
        viewModel.CancelTreeName();

        breads.Children.Should().BeEmpty();
        viewModel.ProductTree.PendingProduct.Should().BeNull();
        viewModel.TreeNameEdit.Should().BeNull();
        viewModel.SelectedTreeNode.Should().BeNull();
        viewModel.SelectedProduct.Should().BeNull("the form stays on the blank new product");

        await RunAsync(viewModel.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));
        await viewModel.CommitTreeNameAsync("  ");
        breads.Children.Should().BeEmpty();

        await RunAsync(viewModel.TreeMenuFor(innovation).Single(i => i.Header == "New Folder"));
        viewModel.CancelTreeName();
        TitlesIn(innovation).Should().Equal("Breads");
        viewModel.ProductTree.PendingFolder.Should().BeNull();
        viewModel.SelectedTreeNode.Should().BeNull();

        await RunAsync(viewModel.TreeMenuFor(innovation).Single(i => i.Header == "New Folder"));
        await viewModel.CommitTreeNameAsync(string.Empty);
        TitlesIn(innovation).Should().Equal("Breads");

        viewModel.ErrorMessage.Should().BeNull();
        await using var dbContext = CreateDbContext();
        (await dbContext.Products.CountAsync(Cancellation)).Should().Be(0);
        (await dbContext.ProductFolders.CountAsync(Cancellation)).Should().Be(1);
    });

    [Test]
    public Task Delete_on_a_folder_asks_first_then_moves_what_it_holds_up_a_level_with_nothing_on_the_form_to_save() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", ProductStage.Innovation, breadsId);
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var bibingkaId = await SaveProductAsync("Bibingka", holidayId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Folder(breadsId)).Single(i => i.Header == "Delete"));

        viewModel.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Breads' folder? Its 1 product and 1 folder move up to Innovation.");
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, (int?)null));
        (await PlaceOfAsync(bibingkaId)).Should().Be((ProductStage.Innovation, holidayId));
        viewModel.ProductTree.Folder(breadsId).Should().BeNull();
        TitlesIn(viewModel.ProductTree.StageFolder(ProductStage.Innovation)).Should().Equal("Holiday", "Mamon");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
        viewModel.History.IsDirty.Should().BeFalse("the folder went in the database: there's nothing to save");
    });

    [Test]
    public Task Delete_on_a_product_asks_first_moves_it_to_Deleted_and_the_form_starts_afresh_only_if_it_was_showing_it() => UiThread.RunAsync(async () =>
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

        question.Should().Be("Delete 'Ensaymada'? It moves to the Deleted folder at the bottom of the list, where it can be restored as it is.");
        viewModel.Products.Select(p => p.Name).Should().Equal("Mamon");
        viewModel.DeletedProducts.Select(p => p.Name).Should().Equal("Ensaymada");
        TitlesIn(viewModel.ProductTree.DeletedFolder).Should().Equal("Ensaymada");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId, "the one showing wasn't the one deleted");

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Delete"));

        viewModel.Products.Should().BeEmpty();
        TitlesIn(viewModel.ProductTree.DeletedFolder).Should().Equal("Ensaymada", "Mamon");
        viewModel.SelectedProduct.Should().BeNull();
        viewModel.EditingName.Should().BeEmpty();
    });

    [Test]
    public Task A_deleted_products_row_leaves_the_form_as_it_is_and_its_menu_restores_it_or_deletes_it_permanently() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", breadsId, ProductStage.Production);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(mamonId, Cancellation);
        var viewModel = await OpenedViewModelAsync(pandesalId);
        var mamon = viewModel.ProductTree.Find(mamonId)!;

        viewModel.SelectedTreeNode = mamon;

        viewModel.SelectedProduct!.Id.Should().Be(pandesalId, "a deleted product isn't opened");
        viewModel.SelectedTreeNode.Should().BeSameAs(mamon, "and its row stays picked");
        viewModel.SelectedTreeProducts.Should().BeEmpty("no live product's menu takes it in");
        var menu = viewModel.TreeMenuFor(mamon);
        Headers(menu).Should().Equal("Restore", "Delete Permanently");
        viewModel.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == viewModel.DeletePermanentlyCommand && i.Parameter == mamon);
        viewModel.TreeShortcut("F2").Should().BeNull();
        viewModel.TreeMenuFor(viewModel.ProductTree.DeletedFolder).Should().BeEmpty("the folder itself has nothing to offer");
        viewModel.ComponentsTable.ComponentProductOptions.Select(p => p.Id).Should().NotContain(mamonId);

        await RunAsync(menu.Single(i => i.Header == "Restore"));

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.DeletedProducts.Should().BeEmpty();
        viewModel.ProductTree.Folder(breadsId)!.Products.Should().ContainSingle(l => l.Product.Id == mamonId, "back where it was");
        viewModel.ProductTree.Find(mamonId).Should().BeSameAs(mamon, "its row moves back with it");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId, "a restored product is opened");

        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(mamonId, Cancellation);
        await viewModel.LoadCommand.Execute();
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Delete Permanently"));

        question.Should().StartWith("Delete 'Mamon' permanently?").And.EndWith("This can't be undone.");
        viewModel.DeletedProducts.Should().BeEmpty();
        viewModel.ProductTree.Find(mamonId).Should().BeNull();
        (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(mamonId, Cancellation)).Should().BeNull();
    });

    [Test]
    public Task A_deleted_product_opened_from_elsewhere_is_picked_in_the_Deleted_folder_and_a_search_finds_it_there() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(mamonId, Cancellation);
        var viewModel = await OpenedViewModelAsync(pandesalId);
        viewModel.ProductTree.DeletedFolder.IsExpanded.Should().BeFalse();

        viewModel.OpenProduct(mamonId);

        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(mamonId));
        viewModel.ProductTree.DeletedFolder.IsExpanded.Should().BeTrue("the row picked shows");
        viewModel.SelectedProduct!.Id.Should().Be(pandesalId, "the form stays on what it showed");

        viewModel.SearchText = "Pan";
        TitlesIn(viewModel.ProductTree.DeletedFolder).Should().BeEmpty();
        viewModel.SearchText = "Mam";
        TitlesIn(viewModel.ProductTree.DeletedFolder).Should().Equal("Mamon");
    });

    [Test]
    public Task The_category_filter_shows_a_categorys_products_wherever_they_are_filed_and_its_choices_follow_the_categories() => UiThread.RunAsync(async () =>
    {
        var breadsCategory = await AddProductCategoryAsync("Breads");
        var cakesCategory = await AddProductCategoryAsync("Cakes");
        var bakeryId = await AddFolderAsync("Bakery", ProductStage.Production);
        var holidayId = await AddFolderAsync("Holiday", ProductStage.Production, bakeryId);
        var trialsId = await AddFolderAsync("Trials");
        var pandesalId = await SaveProductAsync("Pandesal", bakeryId, ProductStage.Production, breadsCategory);
        var bibingkaId = await SaveProductAsync("Bibingka", holidayId, ProductStage.Production, cakesCategory);
        var testBunId = await SaveProductAsync("Test Bun", trialsId, categoryId: breadsCategory);
        var looseId = await SaveProductAsync("Loose Loaf", null, ProductStage.Production);
        var oldBunId = await SaveProductAsync("Old Bun", null, categoryId: breadsCategory);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(oldBunId, Cancellation);
        var viewModel = await LoadedViewModelAsync();
        var tree = viewModel.ProductTree;

        viewModel.CategoryFilters.Select(f => f.Label).Should().Equal("All categories", "Breads", "Cakes", "Uncategorized");
        viewModel.CategoryFilter.Should().Be(new ProductCategoryFilter.All());

        viewModel.CategoryFilter = viewModel.CategoryFilters.Single(f => f.Label == "Breads");

        viewModel.VisibleProducts.Select(p => p.Id).Should().BeEquivalentTo([pandesalId, testBunId]);
        TitlesIn(tree.StageFolder(ProductStage.Production)).Should().Equal(["Bakery"], "only the folders holding a match show");
        TitlesIn(tree.Folder(bakeryId)!).Should().Equal("Pandesal");
        TitlesIn(tree.Folder(trialsId)!).Should().Equal("Test Bun");
        TitlesIn(tree.DeletedFolder).Should().Equal(["Old Bun"], "the Deleted folder is filtered too");
        tree.Folder(bakeryId)!.ProductCount.Should().Be(1);

        viewModel.SearchText = "test";
        viewModel.VisibleProducts.Select(p => p.Id).Should().Equal([testBunId], "with a search, both have to match");
        viewModel.SearchText = string.Empty;

        viewModel.CategoryFilter = new ProductCategoryFilter.Uncategorized();
        viewModel.VisibleProducts.Select(p => p.Id).Should().Equal(looseId);
        TitlesIn(tree.StageFolder(ProductStage.Production)).Should().Equal("Loose Loaf");

        // A product opened from elsewhere that the filter hides shows: the filter goes.
        viewModel.OpenProduct(bibingkaId);
        viewModel.CategoryFilter.Should().Be(new ProductCategoryFilter.All());
        viewModel.SelectedTreeNode.Should().BeSameAs(tree.Find(bibingkaId));

        // Renamed in Settings, the category keeps its place picked; deleted, the filter goes back to All categories.
        viewModel.CategoryFilter = viewModel.CategoryFilters.Single(f => f.Label == "Cakes");
        await new ProductCategoryRepository(CreateDbContextFactory()).UpdateAsync(new ProductCategory { Id = cakesCategory, Name = "Cakes & Tortes" }, Cancellation);
        await viewModel.LoadCommand.Execute();
        viewModel.CategoryFilter.Should().Be(new ProductCategoryFilter.Category(cakesCategory, "Cakes & Tortes"));
        viewModel.VisibleProducts.Select(p => p.Id).Should().Equal(bibingkaId);

        await new ProductCategoryRepository(CreateDbContextFactory()).DeleteAsync(cakesCategory, Cancellation);
        await viewModel.LoadCommand.Execute();
        viewModel.CategoryFilter.Should().Be(new ProductCategoryFilter.All());
        viewModel.CategoryFilters.Select(f => f.Label).Should().Equal("All categories", "Breads", "Uncategorized");

        // New Folder clears the filters, so its row shows.
        viewModel.CategoryFilter = viewModel.CategoryFilters.Single(f => f.Label == "Breads");
        await RunAsync(viewModel.TreeMenuFor(tree.StageFolder(ProductStage.Production)).Single(i => i.Header == "New Folder"));
        viewModel.CategoryFilter.Should().Be(new ProductCategoryFilter.All());
        TitlesIn(tree.StageFolder(ProductStage.Production)).Should().Contain("New folder");
    });

    [Test]
    public Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var viewModel = await LoadedViewModelAsync();
        var mamon = viewModel.ProductTree.Find(mamonId)!;
        var breads = viewModel.ProductTree.Folder(breadsId)!;

        viewModel.SelectedTreeNode = mamon;
        viewModel.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == viewModel.RenameProductCommand && i.Parameter == mamon);
        viewModel.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == viewModel.DeleteProductCommand && i.Parameter == mamon);

        viewModel.SelectedTreeNode = breads;
        viewModel.TreeShortcut("F2")!.Command.Should().BeSameAs(viewModel.RenameFolderCommand);
        viewModel.TreeShortcut("Del")!.Command.Should().BeSameAs(viewModel.DeleteFolderCommand);

        viewModel.SelectedTreeNode = viewModel.ProductTree.StageFolder(ProductStage.Production);
        viewModel.TreeShortcut("F2").Should().BeNull("a stage can't be renamed");
        viewModel.TreeShortcut("Del").Should().BeNull("nor deleted");

        viewModel.SelectedTreeNode = mamon;
        viewModel.SelectedTreeNodes.Add(viewModel.ProductTree.Find(pandesalId)!);
        viewModel.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    });

    [Test]
    public Task A_product_opened_from_elsewhere_is_picked_alone_in_the_list() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        var ensaymadaId = await SaveProductAsync("Ensaymada", null);
        var viewModel = await OpenedViewModelAsync(mamonId);
        viewModel.SelectedTreeNodes.Add(viewModel.ProductTree.Find(pandesalId)!);

        viewModel.OpenProduct(ensaymadaId);

        viewModel.SelectedTreeNodes.Should().Equal(viewModel.ProductTree.Find(ensaymadaId)!);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(ensaymadaId));
    });

    [Test]
    public Task Undoing_a_move_back_into_a_folder_deleted_since_is_refused_and_says_why() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await LoadedViewModelAsync();
        var moveTo = viewModel.TreeMenuFor(viewModel.ProductTree.Find(mamonId)).Single(i => i.Header == "Move to");
        await RunAsync(moveTo.Items.Single(i => i.Header == "Innovation").Items.Single(i => i.Header == "Cakes"));
        await new ProductFolderRepository(CreateDbContextFactory()).DeleteAsync(breadsId, Cancellation);

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("The folder to move it to no longer exists.");
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, cakesId));
    });

    [Test]
    public Task The_folders_left_closed_stay_closed_next_time_and_a_new_folder_opens() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var store = new InMemoryViewStateStore();
        (await LoadedViewModelAsync(store)).Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedViewModelAsync(store);

        first.ProductTree.Folder(breadsId)!.IsExpanded = false;

        await Settle.EventuallyAsync(
            () => store.ReadList(ProductsViewModel.ClosedFoldersKey)?.Contains($"Folder/{breadsId}") == true,
            () => string.Join(", ", store.ReadList(ProductsViewModel.ClosedFoldersKey) ?? []));
        first.Dispose();

        var pastriesId = await AddFolderAsync("Pastries", ProductStage.Production);
        var next = await LoadedViewModelAsync(store);
        next.ProductTree.Folder(breadsId)!.IsExpanded.Should().BeFalse();
        next.ProductTree.Folder(cakesId)!.IsExpanded.Should().BeTrue();
        next.ProductTree.Folder(pastriesId)!.IsExpanded.Should().BeTrue("a folder new since opens, as every folder first did");

        // One closed just before the app closes is written as it closes.
        next.ProductTree.Folder(cakesId)!.IsExpanded = false;
        next.Dispose();
        store.ReadList(ProductsViewModel.ClosedFoldersKey).Should().Contain($"Folder/{cakesId}");
    });

    [Test]
    public Task The_folders_the_list_kept_open_before_open_again_and_the_rest_start_closed() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var store = new InMemoryViewStateStore();
        store.WriteList(ProductsViewModel.OpenFoldersKey, ["Production", $"Folder/{breadsId}"]);

        var viewModel = await LoadedViewModelAsync(store);

        viewModel.ProductTree.Folder(breadsId)!.IsExpanded.Should().BeTrue();
        viewModel.ProductTree.Folder(cakesId)!.IsExpanded.Should().BeFalse();
        viewModel.ProductTree.StageFolder(ProductStage.Innovation).IsExpanded.Should().BeFalse();

        // Kept as closed folders from then on.
        viewModel.Dispose();
        store.ReadList(ProductsViewModel.ClosedFoldersKey).Should().Contain($"Folder/{cakesId}").And.NotContain($"Folder/{breadsId}");
    });

    [Test]
    public Task Innovation_left_closed_under_its_former_name_stays_closed() => UiThread.RunAsync(async () =>
    {
        await AddFolderAsync("Breads");
        var store = new InMemoryViewStateStore();
        store.WriteList(ProductsViewModel.ClosedFoldersKey, ["Deleted", "ResearchAndDevelopment"]);

        var viewModel = await LoadedViewModelAsync(store);

        viewModel.ProductTree.StageFolder(ProductStage.Innovation).IsExpanded.Should().BeFalse("it was left closed before the rename");

        // Kept by its key now once a folder is opened or closed.
        viewModel.ProductTree.StageFolder(ProductStage.Production).IsExpanded = false;
        viewModel.Dispose();
        store.ReadList(ProductsViewModel.ClosedFoldersKey).Should().Contain("Innovation").And.NotContain("ResearchAndDevelopment");
    });

    private static IEnumerable<string> Headers(IEnumerable<TreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    private static IEnumerable<string> TitlesIn(ProductFolderNode folder) => folder.Children.Select(c => c.Title);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    [Test]
    public Task A_product_whose_current_price_earns_less_than_its_target_markup_is_flagged_in_the_list() => UiThread.RunAsync(async () =>
    {
        var fillingId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.25m);
        var (shortId, _) = await AddProductWithSubAssemblyAsync("Ube Pandesal", fillingId, [(ubeId, 100m)], weightPerPiece: 10m);
        var (enoughId, _) = await AddProductWithSubAssemblyAsync("Ube Roll", fillingId, [(ubeId, 100m)], weightPerPiece: 10m);

        // 2.50 of ube a piece and a 30% target: 3.00 earns well under it, 10.00 well over, whatever the VAT.
        await PriceAsync(shortId, currentPrice: 3m, targetMarkupPercent: 30m);
        await PriceAsync(enoughId, currentPrice: 10m, targetMarkupPercent: 30m);

        var viewModel = await LoadedViewModelAsync();
        await Settle.UntilAsync(viewModel, () => viewModel.ProductTree.Find(shortId)!.IsBelowTargetMarkup, () => "not flagged yet");

        viewModel.ProductTree.Find(enoughId)!.IsBelowTargetMarkup.Should().BeFalse();
    });

    private async Task PriceAsync(int productId, decimal currentPrice, decimal targetMarkupPercent)
    {
        await using var dbContext = CreateDbContext();
        var product = await dbContext.Products.SingleAsync(p => p.Id == productId, Cancellation);
        product.CurrentPrice = currentPrice;
        if (await dbContext.CostingParameters.SingleOrDefaultAsync(c => c.ProductId == productId, Cancellation) is { } costing)
        {
            costing.MarginPercent = targetMarkupPercent;
        }
        else
        {
            dbContext.CostingParameters.Add(new CostingParameters { ProductId = productId, MarginPercent = targetMarkupPercent });
        }

        await dbContext.SaveChangesAsync(Cancellation);
    }

    [Test]
    public Task Picking_another_product_over_unsaved_changes_asks_and_Cancel_stays_on_them() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Mamon Tostado";

        Click(viewModel, viewModel.ProductTree.Find(pandesalId)!);
        await Settle.UntilAsync(viewModel, () => asked.Count == 1, () => $"asked={asked.Count}");

        asked.Single().Should().StartWith("Save the changes to Mamon Tostado?");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.ProductTree.Find(mamonId), "the list goes back to the row showing");
        viewModel.EditingName.Should().Be("Mamon Tostado");
        viewModel.History.IsDirty.Should().BeTrue();
    });

    [Test]
    [Arguments(UnsavedChangesAnswer.Save, "Mamon Tostado")]
    [Arguments(UnsavedChangesAnswer.DontSave, "Mamon")]
    public Task Picking_another_product_over_unsaved_changes_opens_it_once_they_are_saved_or_not(UnsavedChangesAnswer answer, string savedName) => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        AnswerUnsavedChanges(viewModel, answer);
        viewModel.EditingName = "Mamon Tostado";

        Click(viewModel, viewModel.ProductTree.Find(pandesalId)!);
        await Settle.UntilAsync(viewModel,
            () => viewModel.SelectedProduct?.Id == pandesalId && viewModel.AreDetailsReady && !viewModel.History.IsDirty,
            () => $"selected={viewModel.SelectedProduct?.Id} ready={viewModel.AreDetailsReady}");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingName.Should().Be("Pandesal");
        (await NameOfAsync(mamonId)).Should().Be(savedName);
    });

    [Test]
    public Task New_over_unsaved_changes_asks_and_Cancel_keeps_them() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Mamon Tostado";

        await viewModel.NewCommand.Execute();

        asked.Should().ContainSingle();
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
        viewModel.EditingName.Should().Be("Mamon Tostado");
        viewModel.ProductTree.PendingProduct.Should().BeNull("no new product's row is added");
        viewModel.TreeNameEdit.Should().BeNull();
    });

    [Test]
    public Task Clone_over_unsaved_changes_asks_first_and_Cancel_makes_nothing() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Mamon Tostado";
        viewModel.EditingCloneName = "Mamon Ube";

        await viewModel.CloneCommand.Execute();

        asked.Should().ContainSingle();
        viewModel.Products.Should().NotContain(p => p.Name == "Mamon Ube");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
        viewModel.EditingName.Should().Be("Mamon Tostado");
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var viewModel = await OpenedViewModelAsync(mamonId);
        AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        viewModel.EditingName = "Mamon Tostado";

        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();

        await Settle.UntilAsync(viewModel,
            () => viewModel.EditingName == "Mamon" && viewModel.AreDetailsReady && !viewModel.History.IsDirty,
            () => $"name={viewModel.EditingName}");
        viewModel.SelectedProduct!.Id.Should().Be(mamonId);
        (await NameOfAsync(mamonId)).Should().Be("Mamon");
    });

    // No puts a new product back blank. What's typed into it next has to count, or it's lost with no question asked.
    [Test]
    public Task What_is_typed_after_no_on_a_new_product_counts_as_unsaved() => UiThread.RunAsync(async () =>
    {
        var viewModel = await LoadedViewModelAsync();
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        viewModel.EditingName = "Mamon";
        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        viewModel.EditingName.Should().BeEmpty();
        viewModel.History.IsDirty.Should().BeFalse();
        viewModel.History.CanUndo.Should().BeFalse("what was thrown away isn't there to undo back to");

        viewModel.EditingName = "Mamon Tostado";

        viewModel.History.IsDirty.Should().BeTrue();
        await viewModel.ResolveUnsavedChangesAsync(revert: false);
        asked.Should().HaveCount(2);
    });

    /// <summary>Answers the question about unsaved changes with <paramref name="answer"/>, collecting each question asked.</summary>
    private static List<string> AnswerUnsavedChanges(ProductsViewModel viewModel, UnsavedChangesAnswer answer)
    {
        List<string> asked = [];
        viewModel.ConfirmUnsavedChanges.RegisterHandler(context =>
        {
            asked.Add(context.Input);
            context.SetOutput(answer);
        });
        return asked;
    }

    /// <summary>A click on <paramref name="row"/>, as the tree makes one: the rows picked replaced by it.</summary>
    private static void Click(ProductsViewModel viewModel, object row)
    {
        viewModel.SelectedTreeNodes.Clear();
        viewModel.SelectedTreeNodes.Add(row);
    }

    private async Task<string> NameOfAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Products.Where(p => p.Id == productId).Select(p => p.Name).SingleAsync(Cancellation);
    }

    private static async Task RunAsync(TreeMenuItem item)
    {
        switch (item.Command)
        {
            case ReactiveCommand<IReadOnlyList<ProductMove>, RxVoid> move:
                await move.Execute((IReadOnlyList<ProductMove>)item.Parameter!);
                break;
            case ReactiveCommand<StageMoveRequest, RxVoid> toStage:
                await toStage.Execute((StageMoveRequest)item.Parameter!);
                break;
            case ReactiveCommand<ProductFolderMove, RxVoid> moveFolder:
                await moveFolder.Execute((ProductFolderMove)item.Parameter!);
                break;
            case ReactiveCommand<IReadOnlyList<ProductTreeNode>, RxVoid> onRows:
                await onRows.Execute((IReadOnlyList<ProductTreeNode>)item.Parameter!);
                break;
            case ReactiveCommand<ProductTreeNode, RxVoid> onRow:
                await onRow.Execute((ProductTreeNode)item.Parameter!);
                break;
            case ReactiveCommand<RxVoid, RxVoid> plain:
                await plain.Execute();
                break;
            case ReactiveCommand<IReadOnlyList<Product>, RxVoid> onProducts:
                await onProducts.Execute((IReadOnlyList<Product>)item.Parameter!);
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

    /// <summary>A folder of the list, in <paramref name="stage"/> (Innovation, as a product saved here is), inside <paramref name="parentId"/> or at the top.</summary>
    private async Task<int> AddFolderAsync(string name, ProductStage stage = ProductStage.Innovation, int? parentId = null)
    {
        var folder = new ProductFolder { Name = name, Stage = stage, ParentId = parentId };
        await new ProductFolderRepository(CreateDbContextFactory()).AddAsync(folder, Cancellation);
        return folder.Id;
    }

    private async Task<int?> FolderIdAsync(string name, ProductStage? stage = null)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.ProductFolders
            .Where(f => f.Name == name && (stage == null || f.Stage == stage))
            .Select(f => (int?)f.Id)
            .SingleAsync(Cancellation);
    }

    private async Task<ProductFolder> SingleFolderAsync(string name)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.ProductFolders.SingleAsync(f => f.Name == name, Cancellation);
    }

    /// <summary>A product with nothing but its name, folder, stage and category, saved as the Products screen saves a new one.</summary>
    private Task<int> SaveProductAsync(string name, int? folderId, ProductStage stage = ProductStage.Innovation, int? categoryId = null) =>
        new ProductRepository(CreateDbContextFactory()).SaveAsync(
            new Product { Name = name, FolderId = folderId, CategoryId = categoryId, Stage = stage }, recipe: null, packagingLink: null, packagingLines: null, costing: null, Cancellation);

    private async Task<(ProductStage Stage, int? FolderId)> PlaceOfAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        var product = await dbContext.Products.SingleAsync(p => p.Id == productId, Cancellation);
        return (product.Stage, product.FolderId);
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
        await Settle.UntilAsync(viewModel, () => viewModel.AreDetailsReady && viewModel.SelectedProduct?.Id == productId, () => $"ready={viewModel.AreDetailsReady}");
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
            new PackagingLinkRepository(factory),
            new ProductCloner(factory),
            CreateUnitOfWork(),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new ProductCategoryRepository(factory),
            new ProductFolderRepository(factory),
            new ProductVersionRepository(factory),
            new ProductValidator(),
            new CostingParametersValidator(),
            new ProductCategoryValidator(),
            viewStateStore ?? new InMemoryViewStateStore(),
            new LoggerConfiguration().CreateLogger());
    }
}
