using System.Windows.Input;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// The Products list's right-click menus, its keys, its selection and its category filter: what each menu offers a
/// product, several products, a folder and the list itself, and what each item does, run as the menu runs it. Moves,
/// renames and folders save at once; the notice of a move can take it back; the folders left open open again next time.
/// </summary>
[NotInParallel(DatabaseCollection.C)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.C)]
public class ProductsStateTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_products_menu_moves_it_to_the_other_stage_or_any_folder_and_offers_what_else_can_be_done_with_it() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        await AddFolderAsync("Cakes");
        var bakeryId = await AddFolderAsync("Bakery", ProductStage.Production);
        await AddFolderAsync("Rolls", ProductStage.Production, bakeryId);
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await LoadedStateAsync();
        var leaf = state.View.List.ProductTree.Find(mamonId)!;
        state.View.List.SelectedTreeNode = leaf;

        var menu = state.View.List.TreeMenuFor(leaf);

        Headers(menu).Should().Equal("Promote to Production", "Move to", "Cut", "Paste", "Apply Costing Defaults…", "Print", "Rename", "Clone…", "Create Variant…", "Delete");
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
        state.View.List.MoveNotice.Should().Be("Moved Mamon to Production › Bakery › Rolls");

        state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId))[0].Header
            .Should().Be("Send Back to Innovation", "in Production it can only go back");
    });

    [Test]
    public Task A_drop_moves_products_or_one_folder_onto_a_folder_and_gives_the_first_back_to_bring_into_view() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await LoadedStateAsync();
        IListTreeScreen screen = state.View.List;
        var mamon = state.View.List.ProductTree.Find(mamonId)!;
        var breads = state.View.List.ProductTree.Folder(breadsId)!;
        var cakes = state.View.List.ProductTree.Folder(cakesId)!;
        var production = state.View.List.ProductTree.StageFolder(ProductStage.Production);

        screen.CanDrag(mamon).Should().BeTrue();
        screen.CanDrag(cakes).Should().BeTrue("one folder is dragged with all it holds");
        screen.CanDrop([mamon, cakes], production).Should().BeFalse("not a folder and products together");
        screen.CanDrag(production).Should().BeFalse("a stage is fixed");
        screen.CanDrop([mamon], breads).Should().BeFalse("it's there already");
        screen.CanDrop([mamon], state.View.List.ProductTree.DeletedFolder).Should().BeFalse("nothing is dropped into Deleted");
        screen.CanDrop([mamon], cakes).Should().BeTrue();

        (await DropAsync(state, [mamon], cakes)).Should().BeSameAs(mamon, "its row keeps its node, in its new folder");

        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, cakesId));
        cakes.Products.Should().ContainSingle();
        state.View.List.MoveNotice.Should().Be("Moved Mamon to Innovation › Cakes");
        (await DropAsync(state, [mamon], cakes)).Should().BeNull("a drop that moves nothing has no row to show");

        // A folder dropped in the other stage takes what it holds there.
        screen.CanDrop([cakes], cakes).Should().BeFalse("a folder doesn't go inside itself");
        (await DropAsync(state, [cakes], production)).Should().BeSameAs(cakes);

        state.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, cakesId), "a product goes with its folder");
        state.View.List.MoveNotice.Should().Be("Moved Cakes to Production");
        production.Folders.Should().Contain(cakes);
        cakes.Stage.Should().Be(ProductStage.Production);

        await state.View.List.UndoMoveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, cakesId));
        state.View.List.ProductTree.StageFolder(ProductStage.Innovation).Folders.Should().Contain(cakes, "Undo puts the folder back with it");
    });

    [Test]
    public Task A_drop_made_while_a_move_is_still_saving_does_nothing() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await LoadedStateAsync();
        IListTreeScreen screen = state.View.List;

        // Breads to Production: it saves, then reads the list again. A drop meanwhile would be judged against the list as
        // it was, and the two would take each other's Undo.
        var folderMove = state.View.List.MoveFolderCommand.ExecuteAsync(new ProductFolderMove(breadsId, ProductStage.Production, null));
        state.View.List.MoveProductsCommand.CanExecute([]).Should().BeFalse("the folder's move is under way, as a drop would find it");
        (await screen.DropAsync([state.View.List.ProductTree.Find(mamonId)!], state.View.List.ProductTree.Folder(cakesId))).Should().BeNull("a move is still saving");
        await folderMove;

        state.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, breadsId), "the drop did nothing: Mamon went with its folder alone");
    });

    [Test]
    public Task Promote_files_it_at_the_same_path_in_Production_and_Undo_takes_away_the_folders_it_made() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var holidayId = await AddFolderAsync("Holiday", ProductStage.Innovation, breadsId);
        var productionBreadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", holidayId);
        var state = await LoadedStateAsync();

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header == "Promote to Production"));

        state.ErrorMessage.Should().BeNull();
        var (stage, folderId) = await PlaceOfAsync(mamonId);
        stage.Should().Be(ProductStage.Production);
        var made = state.View.List.ProductTree.Folder(folderId!.Value)!;
        made.Title.Should().Be("Holiday", "Holiday was made in Production's Breads, which was there already");
        made.ParentFolderId.Should().Be(productionBreadsId);
        made.Products.Should().ContainSingle();
        state.View.List.MoveNotice.Should().Be("Moved Mamon to Production › Breads › Holiday");

        await state.View.List.UndoMoveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, holidayId));
        state.View.List.ProductTree.Folder(holidayId)!.Products.Should().ContainSingle();
        state.View.List.ProductTree.Folder(made.FolderId!.Value).Should().BeNull("the folder the Promote made goes, empty again");
        state.View.List.ProductTree.Folder(productionBreadsId).Should().NotBeNull("one it found there stays");
        state.View.List.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        state.View.List.UndoMoveCommand.CanExecute().Should().BeFalse();
    });

    [Test]
    public Task A_Promote_counts_only_what_the_database_moved_when_another_computer_moved_one_first() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var state = await LoadedStateAsync();
        state.View.List.SelectedTreeNode = state.View.List.ProductTree.Find(mamonId);
        state.View.List.SelectedTreeNodes.Add(state.View.List.ProductTree.Find(pandesalId)!);
        var promote = state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header == "Promote 2 Products to Production");

        // Promoted elsewhere while this list still shows them in Innovation.
        await new ProductRepository(CreateDbContextFactory()).MoveToStageAsync([pandesalId], ProductStage.Production, Cancellation);
        await RunAsync(promote);

        state.ErrorMessage.Should().BeNull();
        state.View.List.MoveNotice.Should().Be("Moved Mamon to Production › Breads");
        await state.View.List.UndoMoveCommand.ExecuteAsync();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId));
        (await PlaceOfAsync(pandesalId)).Stage.Should().Be(ProductStage.Production, "Undo puts back only what this Promote moved");

        // With nothing left to move, nothing is said and there's nothing to undo.
        var again = state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header.StartsWith("Promote", StringComparison.Ordinal));
        await new ProductRepository(CreateDbContextFactory()).MoveToStageAsync([mamonId], ProductStage.Production, Cancellation);
        await RunAsync(again);

        state.ErrorMessage.Should().BeNull();
        state.View.List.MoveNotice.Should().BeNull();
    });

    [Test]
    public Task Cut_marks_the_products_picked_and_Paste_on_a_folder_moves_them_there_with_Undo() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var ensaymadaId = await SaveProductAsync("Ensaymada", breadsId);
        var state = await LoadedStateAsync();
        var tree = state.View.List.ProductTree;
        var cakes = tree.Folder(cakesId)!;
        state.View.List.SelectedTreeNode = tree.Find(mamonId);
        state.View.List.SelectedTreeNodes.Add(tree.Find(pandesalId)!);

        // Ctrl+X with both picked: the menu of the first, which covers both.
        await RunAsync(state.View.List.TreeShortcut("Ctrl+X")!);

        state.View.List.CutNodes.Should().Equal(tree.Find(mamonId)!, tree.Find(pandesalId)!);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId), "nothing moves until the paste");
        state.View.List.TreeMenuFor(tree.Find(ensaymadaId)).Single(i => i.Header == "Paste").IsEnabled.Should().BeFalse("they're in that folder already");
        var paste = state.View.List.TreeMenuFor(cakes).Single(i => i.Header == "Paste");
        paste.IsEnabled.Should().BeTrue();

        // Ctrl+V with the folder picked: what its menu's Paste does.
        state.View.List.SelectedTreeNode = cakes;
        state.View.List.TreeShortcut("Ctrl+V").Should().Match<TreeMenuItem>(i => i.Command == state.View.List.PasteCommand && i.Parameter == cakes);
        await RunAsync(paste);

        state.ErrorMessage.Should().BeNull();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, cakesId));
        (await PlaceOfAsync(pandesalId)).Should().Be((ProductStage.Production, cakesId));
        (await PlaceOfAsync(ensaymadaId)).Should().Be((ProductStage.Innovation, breadsId));
        state.View.List.CutNodes.Should().BeEmpty("a paste uses up the cut");
        state.View.List.MoveNotice.Should().Be("Moved 2 products to Production › Cakes");
        state.View.List.TreeMenuFor(cakes).Single(i => i.Header == "Paste").IsEnabled.Should().BeFalse();

        await state.View.List.UndoMoveCommand.ExecuteAsync();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId));
        await MovesOfferedAgainAsync(state);

        // A folder is cut with all it holds, alone; Esc forgets a cut.
        var breads = tree.Folder(breadsId)!;
        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "Cut"));
        state.View.List.CutNodes.Should().Equal(breads);
        state.View.List.TreeMenuFor(tree.Find(mamonId)).Single(i => i.Header == "Paste").IsEnabled.Should().BeFalse("not inside itself");
        state.View.List.SelectedTreeNode = breads;
        await RunAsync(state.View.List.TreeShortcut("Esc")!);
        state.View.List.CutNodes.Should().BeEmpty();
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, breadsId));

        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "Cut"));
        await RunAsync(state.View.List.TreeMenuFor(cakes).Single(i => i.Header == "Paste"));
        state.ErrorMessage.Should().BeNull();
        breads.ParentFolder.Should().BeSameAs(cakes);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, breadsId), "its products go with it, to the other stage");
        state.View.List.TreeShortcut("Esc").Should().BeNull("there's nothing cut to forget");
    });

    [Test]
    public Task Several_products_picked_move_together_and_the_one_showing_stays_on_the_form() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var sansRivalId = await SaveProductAsync("Sans Rival", cakesId, ProductStage.Production);
        var state = await OpenedStateAsync(mamonId);
        var tree = state.View.List.ProductTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        state.View.List.SelectedTreeNodes.Add(tree.Find(pandesalId)!);
        state.View.List.SelectedTreeNodes.Add(tree.Find(sansRivalId)!);
        state.SelectedProduct!.Id.Should().Be(mamonId);
        state.View.List.SelectedTreeNode.Should().BeSameAs(tree.Find(mamonId));

        var menu = state.View.List.TreeMenuFor(tree.Find(pandesalId));

        Headers(menu).Should().Equal(
            "Promote 2 Products to Production", "Send 1 Product Back to Innovation", "Move 3 Products to", "Cut 3 Products", "Paste",
            "Apply Costing Defaults to 3 Products…", "Print 3 Products");
        await RunAsync(menu[0]);

        state.ErrorMessage.Should().BeNull();
        var productionBreadsId = await FolderIdAsync("Breads", ProductStage.Production);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, productionBreadsId));
        (await PlaceOfAsync(pandesalId)).Should().Be(((ProductStage, int?))(ProductStage.Production, productionBreadsId), "one folder is made for both");
        (await PlaceOfAsync(sansRivalId)).Should().Be((ProductStage.Production, cakesId), "it was in Production already");
        state.View.List.MoveNotice.Should().Be("Moved 2 products to Production › Breads");
        state.View.List.SelectedTreeProducts.Select(l => l.Product.Id).Should().Equal(mamonId, pandesalId, sansRivalId);
        state.SelectedProduct!.Id.Should().Be(mamonId);
    });

    [Test]
    public Task Apply_Costing_Defaults_sets_the_products_picked_to_them_once_confirmed_and_the_one_showing_follows() => UiThread.RunAsync(async () =>
    {
        await new CostingDefaultsRepository(CreateDbContextFactory()).SetAsync(
            new CostingDefaults { LaborPercent = 35m, OverheadPercent = 25m, AdministrativePercent = 15m, MarkupPercent = 60m }, Cancellation);
        var mamonId = await SaveProductAsync("Mamon", null);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        var state = await OpenedStateAsync(mamonId);
        string? asked = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(true);
        });
        state.View.List.SelectedTreeNodes.Add(state.View.List.ProductTree.Find(pandesalId)!);

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(pandesalId)).Single(i => i.Header == "Apply Costing Defaults to 2 Products…"));

        state.ErrorMessage.Should().BeNull();
        asked.Should().StartWith("Set the costing of Mamon and Pandesal to the costing defaults: labor 35%, overhead 25%, administrative 15% and markup 60%?");
        state.SuccessMessage.Should().Be("The costing defaults were applied to Mamon and Pandesal, with a version of each.");
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.Costing.EditingMarkupPercent == 60m,
            () => $"ready={state.AreDetailsReady}, markup={state.Costing.EditingMarkupPercent}");
        state.Costing.EditingLaborPercent.Should().Be(35m, "the product showing reads its new costing");
        state.HasUnsavedChanges.Should().BeFalse("its new costing is how it's saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.CostingParameters.Where(c => c.ProductId == pandesalId).Select(c => c.MarkupPercent).SingleAsync(Cancellation)).Should().Be(60m);
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
        var state = await LoadedStateAsync();
        var breads = state.View.List.ProductTree.Folder(breadsId)!;

        var menu = state.View.List.TreeMenuFor(breads);

        Headers(menu).Should().Equal(
            "New Product Here", "New Composite Product Here", "New Folder", "Promote All to Production", "Move to", "Cut", "Paste", "Print Folder",
            "Rename", "Delete", "Expand All", "Collapse All");
        Headers(state.View.List.TreeMenuFor(state.View.List.ProductTree.Folder(cakesId)))
            .Should().NotContain("Promote All to Production", "there's nothing in it to promote");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        Headers(moveTo.Single(i => i.Header == "Innovation").Items).Should().Equal(["Innovation"], "not itself, nor the folders in it");
        Headers(moveTo.Single(i => i.Header == "Production").Items).Should().Equal("Production", "Cakes");

        await RunAsync(menu.Single(i => i.Header == "Promote All to Production"));

        state.ErrorMessage.Should().BeNull();
        var productionBreadsId = await FolderIdAsync("Breads", ProductStage.Production);
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Production, productionBreadsId));
        (await PlaceOfAsync(bibingkaId)).Stage.Should().Be(ProductStage.Production, "the folders in it go too");
        state.View.List.ProductTree.Find(bibingkaId)!.Should().Match<ProductLeafNode>(l => state.View.List.ProductTree.Folder(l.Product.FolderId!.Value)!.Title == "Holiday");
        (await PlaceOfAsync(testLoafId)).Should().Be((ProductStage.Innovation, (int?)null));
        state.View.List.MoveNotice.Should().Be("Moved 2 products to Production");
    });

    [Test]
    public Task The_stage_and_list_menus_add_products_and_folders_and_open_or_close_every_folder() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        await AddFolderAsync("Holiday", ProductStage.Production, breadsId);
        await AddFolderAsync("Trials");
        var state = await LoadedStateAsync();
        var production = state.View.List.ProductTree.StageFolder(ProductStage.Production);
        var innovation = state.View.List.ProductTree.StageFolder(ProductStage.Innovation);

        Headers(state.View.List.TreeMenuFor(production)).Should().Equal("New Product Here", "New Composite Product Here", "New Folder", "Paste", "Print Stage", "Expand All", "Collapse All");
        Headers(state.View.List.TreeMenuFor(null)).Should().Equal("New Product", "New Composite Product", "New Folder", "Print All Products", "Expand All", "Collapse All");

        await RunAsync(state.View.List.TreeMenuFor(null).Single(i => i.Header == "Collapse All"));
        state.Folders.Select(f => state.View.List.ProductTree.Folder(f.Id)!).Should().OnlyContain(f => !f.IsExpanded);
        state.View.List.ProductTree.Stages.Should().OnlyContain(r => r.IsExpanded);

        await RunAsync(state.View.List.TreeMenuFor(production).Single(i => i.Header == "Expand All"));
        state.Folders.Where(f => f.Stage == ProductStage.Production).Select(f => state.View.List.ProductTree.Folder(f.Id)!).Should().OnlyContain(f => f.IsExpanded, "however deep");
        innovation.Folders.Should().OnlyContain(f => !f.IsExpanded, "the other stage's folders are left as they were");

        await RunAsync(state.View.List.TreeMenuFor(null).Single(i => i.Header == "Expand All"));
        state.Folders.Select(f => state.View.List.ProductTree.Folder(f.Id)!).Should().OnlyContain(f => f.IsExpanded);
    });

    [Test]
    public Task While_a_move_saves_no_other_move_nor_its_Undo_can_start_from_a_menu() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await LoadedStateAsync();
        await state.View.List.MoveProductsCommand.ExecuteAsync([new ProductMove(mamonId, ProductStage.Innovation, cakesId)]);
        state.View.List.MoveNotice.Should().NotBeNull("there's a move to undo");
        static bool CanRun(ICommand command) => command.CanExecute(null);

        // Breads to Production: it saves, then reads the list again. Meanwhile a menu's Promote, Send Back or Move to, a
        // folder's own move, and the notice's Undo are all off, as each would be judged against the list as it was.
        var folderMove = state.View.List.MoveFolderCommand.ExecuteAsync(new ProductFolderMove(breadsId, ProductStage.Production, null));
        state.View.List.MoveFolderCommand.CanExecute(new ProductFolderMove(breadsId, ProductStage.Production, null)).Should().BeFalse();
        CanRun(state.View.List.MoveToStageCommand).Should().BeFalse();
        CanRun(state.View.List.MoveProductsCommand).Should().BeFalse();
        CanRun(state.View.List.MoveFolderCommand).Should().BeFalse();
        CanRun(state.View.List.UndoMoveCommand).Should().BeFalse();

        await folderMove;
        state.ErrorMessage.Should().BeNull();

        // The moves are free again once the folder's move has ended.
        CanRun(state.View.List.MoveToStageCommand).Should().BeTrue();
        CanRun(state.View.List.MoveProductsCommand).Should().BeTrue();
        CanRun(state.View.List.MoveFolderCommand).Should().BeTrue();
        CanRun(state.View.List.UndoMoveCommand).Should().BeTrue("the folder's move has a notice to undo");
    });

    [Test]
    public Task A_new_products_place_follows_its_folder_renamed_and_moved_to_the_other_stage_before_its_Save() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var state = await LoadedStateAsync();
        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Folder(breadsId)!).Single(i => i.Header == "New Product Here"));
        state.View.List.CancelTreeName();
        state.NewProductTag.Should().Be("Goes in Innovation › Breads");

        // Renamed and sent to Production, from another computer, then read again here.
        var folders = new ProductFolderRepository(CreateDbContextFactory());
        await folders.RenameAsync(breadsId, "Bread Rolls", Cancellation);
        await folders.MoveAsync(new ProductFolderMove(breadsId, ProductStage.Production, null), Cancellation);
        await state.LoadCommand.ExecuteAsync();

        state.NewProductTag.Should().Be("Goes in Production › Bread Rolls", "the header says where Save will file it now");
        state.NewProductPlace.Stage.Should().Be(ProductStage.Production);
        TypeName(state, "Pan de Coco");
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        var saved = await ProductNamedAsync("Pan de Coco");
        (saved.Stage, saved.FolderId).Should().Be((ProductStage.Production, breadsId));
    });

    [Test]
    public Task New_Product_Here_starts_a_product_in_the_folder_that_Save_files_there_with_no_category() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var state = await LoadedStateAsync();
        state.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        var breads = state.View.List.ProductTree.Folder(breadsId)!;
        state.NewProductTag.Should().Be("Goes in Production", "the blank form is a new product too, in Settings' stage");

        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));

        state.SelectedProduct.Should().BeNull();
        state.EditingCategory.Should().BeNull("a folder isn't a category");
        state.NewProductTag.Should().Be("Goes in Innovation › Breads", "the header says where Save will file it");
        state.History.IsDirty.Should().BeFalse("nothing is typed yet");

        // Revert keeps the folder it was started from.
        state.View.List.CancelTreeName();
        TypeName(state, "Something Else");
        await state.RevertCommand.ExecuteAsync();
        state.EditingName.Should().BeEmpty();
        state.NewProductTag.Should().Be("Goes in Innovation › Breads");

        TypeName(state, "Pan de Coco");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        var saved = await ProductNamedAsync("Pan de Coco");
        (saved.Stage, saved.FolderId, saved.CategoryId).Should().Be((ProductStage.Innovation, breadsId, (int?)null));
        breads.Products.Select(l => l.Title).Should().Equal("Pan de Coco");
        state.NewProductTag.Should().BeNull("a saved product shows no tag");

        // New, from the header, starts one in the stage Settings gives a new product: Production, to start with.
        await state.NewCommand.ExecuteAsync();
        state.NewProductTag.Should().Be("Goes in Production");
        state.View.List.CancelTreeName();
        TypeName(state, "Test Loaf");
        await state.SaveCommand.ExecuteAsync();

        var plain = await ProductNamedAsync("Test Loaf");
        (plain.Stage, plain.FolderId).Should().Be((ProductStage.Production, (int?)null));
    });

    [Test]
    public Task New_files_a_product_in_the_stage_Settings_gives_a_new_one_and_follows_it_when_it_changes() => UiThread.RunAsync(async () =>
    {
        var defaults = new CostingDefaultsRepository(CreateDbContextFactory());
        await defaults.SetAsync(new CostingDefaults { NewProductStage = ProductStage.Innovation }, Cancellation);
        var state = await LoadedStateAsync();
        state.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));

        state.NewProductTag.Should().Be("Goes in Innovation");
        TypeName(state, "Test Loaf");
        await state.SaveCommand.ExecuteAsync();
        state.ErrorMessage.Should().BeNull();
        (await ProductNamedAsync("Test Loaf")).Stage.Should().Be(ProductStage.Innovation);

        // Changed in Settings while a new product is being typed: its stage was never picked, so it follows. Revert keeps it.
        await state.NewCommand.ExecuteAsync();
        TypeName(state, "Pan de Coco");
        await defaults.SetAsync(new CostingDefaults { NewProductStage = ProductStage.Production }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.NewProductTag.Should().Be("Goes in Production");
        state.EditingName.Should().Be("Pan de Coco", "only the stage follows the setting");
        await state.RevertCommand.ExecuteAsync();
        state.NewProductTag.Should().Be("Goes in Production");

        // A product started from a folder keeps that folder's stage whatever the setting says.
        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.StageFolder(ProductStage.Innovation)).Single(i => i.Header == "New Product Here"));
        await defaults.SetAsync(new CostingDefaults { NewProductStage = ProductStage.Production }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.NewProductTag.Should().Be("Goes in Innovation");
    });

    [Test]
    public Task Rename_types_the_name_in_place_saves_it_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var state = await OpenedStateAsync(mamonId);
        TypeDescription(state, "Toasted and sugared");
        var mamon = state.View.List.ProductTree.Find(mamonId)!;

        await RunAsync(state.View.List.TreeMenuFor(mamon).Single(i => i.Header == "Rename"));

        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(mamon, "Mamon"));
        state.View.List.SelectedTreeNode.Should().BeSameAs(mamon);

        await state.View.List.CommitTreeNameAsync("  Mamon Tostado ");
        await state.View.List.CommitTreeNameAsync("Mamon Again");

        state.ErrorMessage.Should().BeNull();
        state.View.List.TreeNameEdit.Should().BeNull();
        var saved = await ProductNamedAsync("Mamon Tostado");
        saved.Description.Should().BeNull("the rename saves the name alone");
        state.Products.Should().NotContain(p => p.Name == "Mamon Again", "leaving the box after Enter saves nothing more");
        state.EditingName.Should().Be("Mamon Tostado");
        state.EditingCloneName.Should().Be("Mamon Tostado Copy", "the clone's name was still the one it started with");
        state.View.List.ProductTree.Find(mamonId)!.Title.Should().Be("Mamon Tostado");
        state.History.IsDirty.Should().BeTrue("the description is still to be saved");

        state.TryUndo().Should().BeTrue();

        state.EditingDescription.Should().BeEmpty();
        state.EditingName.Should().Be("Mamon Tostado", "no undo takes back a rename that's saved");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Esc_an_empty_name_the_same_name_or_a_search_leave_a_rename_unsaved() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var state = await LoadedStateAsync();
        var mamon = state.View.List.ProductTree.Find(mamonId)!;
        state.View.List.SelectedTreeNode = mamon;

        // F2 does what the menu's Rename does.
        await RunAsync(state.View.List.TreeShortcut("F2")!);
        state.View.List.TreeNameEdit!.Node.Should().BeSameAs(mamon);
        state.View.List.CancelTreeName();
        state.View.List.TreeNameEdit.Should().BeNull();

        await RunAsync(state.View.List.TreeShortcut("F2")!);
        await state.View.List.CommitTreeNameAsync("   ");
        state.View.List.TreeNameEdit.Should().BeNull("an empty name is an Esc");

        await RunAsync(state.View.List.TreeShortcut("F2")!);
        await state.View.List.CommitTreeNameAsync("Mamon");
        state.View.List.TreeNameEdit.Should().BeNull();

        await RunAsync(state.View.List.TreeShortcut("F2")!);
        state.View.List.SearchText = "mam";
        state.View.List.TreeNameEdit.Should().BeNull("a search may hide the row");

        state.View.List.SearchText = string.Empty;
        await RunAsync(state.View.List.TreeShortcut("F2")!);
        state.View.List.CategoryFilter = new ProductCategoryFilter.Uncategorized();
        state.View.List.TreeNameEdit.Should().BeNull("and so may the category filter");

        state.ErrorMessage.Should().BeNull();
        (await NameOfAsync(mamonId)).Should().Be("Mamon");
    });

    [Test]
    public Task A_name_refused_shows_why_and_brings_the_box_back_with_it() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        await AddFolderAsync("Cakes");
        await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var state = await LoadedStateAsync();

        // A new product named as another is.
        await state.NewCommand.ExecuteAsync();
        var row = state.View.List.ProductTree.PendingProduct!;
        await state.View.List.CommitTreeNameAsync("Mamon");

        state.ErrorMessage.Should().NotBeNull();
        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(row, "Mamon"));
        state.View.List.ProductTree.PendingProduct.Should().BeSameAs(row);
        state.SelectedProduct.Should().BeNull();
        state.View.List.CancelTreeName();
        state.EditingName.Should().BeEmpty("Esc leaves the new product blank, as New does");
        state.Products.Should().HaveCount(2);

        // A product renamed as another is.
        var pandesal = state.View.List.ProductTree.Find(pandesalId)!;
        await RunAsync(state.View.List.TreeMenuFor(pandesal).Single(i => i.Header == "Rename"));
        await state.View.List.CommitTreeNameAsync("Mamon");

        state.ErrorMessage.Should().NotBeNull();
        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(pandesal, "Mamon"));
        (await NameOfAsync(pandesalId)).Should().Be("Pandesal");
        state.View.List.CancelTreeName();

        // A folder renamed as one beside it is, whatever the case.
        var breads = state.View.List.ProductTree.Folder(breadsId)!;
        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "Rename"));
        await state.View.List.CommitTreeNameAsync("cakes");

        state.ErrorMessage.Should().Be("Innovation already has a folder named cakes.");
        state.View.List.TreeNameEdit!.Node.Should().BeSameAs(breads);
        state.View.List.TreeNameEdit.InitialText.Should().Be("cakes");
        state.Folders.Select(f => f.Name).Should().Equal("Breads", "Cakes");
    });

    [Test]
    public Task Clone_and_Create_Variant_ask_for_the_new_products_name_and_then_show_it_in_Innovation_at_its_folders_path() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", breadsId, ProductStage.Production);
        var state = await LoadedStateAsync();
        var answer = (string?)"Mamon Mini";
        TextPromptRequest? asked = null;
        state.View.List.PromptForName.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(answer);
        });

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header == "Clone…"));

        // The clone opens in a tab of its own.
        state.ErrorMessage.Should().BeNull();
        asked!.Title.Should().Be("Clone Product");
        asked.InitialValue.Should().Be("Mamon Copy");
        var clone = state.View.Form!;
        clone.SelectedProduct!.Name.Should().Be("Mamon Mini");
        clone.SelectedProduct.Stage.Should().Be(ProductStage.Innovation);
        clone.SelectedProduct.FolderId.Should().Be(await FolderIdAsync("Breads", ProductStage.Innovation), "a Breads folder is made in Innovation for it");
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.ProductTree.Find(clone.SelectedProduct.Id));

        answer = null;
        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header == "Create Variant…"));

        asked.Title.Should().Be("Create Variant");
        state.Products.Should().HaveCount(2, "a cancelled prompt makes nothing");
    });

    [Test]
    public Task New_Folder_names_a_folder_in_place_inside_the_one_it_was_asked_on_and_picks_it() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId, ProductStage.Production);
        var state = await LoadedStateAsync();
        var breads = state.View.List.ProductTree.Folder(breadsId)!;

        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "New Folder"));

        var folder = state.View.List.ProductTree.PendingFolder!;
        TitlesIn(breads).Should().Equal("New folder", "Pandesal");
        state.View.List.SelectedTreeNode.Should().BeSameAs(folder);
        state.View.List.TreeNameEdit!.Node.Should().BeSameAs(folder);
        state.View.List.TreeNameEdit.InitialText.Should().Be("New folder");
        state.View.List.TreeNameEdit.Hint.Should().Contain("isn't a category");
        state.View.List.TreeMenuFor(folder).Should().BeEmpty();

        await state.LoadCommand.ExecuteAsync();
        TitlesIn(breads).Should().Equal(["New folder", "Pandesal"], "a load keeps it");

        await state.View.List.CommitTreeNameAsync("Holiday");

        state.ErrorMessage.Should().BeNull();
        state.View.List.ProductTree.PendingFolder.Should().BeNull();
        var holiday = await SingleFolderAsync("Holiday");
        (holiday.Stage, holiday.ParentId).Should().Be((ProductStage.Production, breadsId));
        TitlesIn(breads).Should().Equal("Holiday", "Pandesal");
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.ProductTree.Folder(holiday.Id));
        state.Categories.Should().BeEmpty("a folder adds no category");

        // From the list's empty space, in the folder of the product picked; a name taken there is refused.
        state.View.List.SelectedTreeNode = state.View.List.ProductTree.Find(pandesalId);
        await RunAsync(state.View.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        state.View.List.ProductTree.PendingFolder!.ParentFolderId.Should().Be(breadsId);
        await state.View.List.CommitTreeNameAsync("holiday");

        state.ErrorMessage.Should().Be("Production › Breads already has a folder named holiday.");
        state.View.List.TreeNameEdit!.InitialText.Should().Be("holiday", "the box comes back with it");
        state.View.List.ProductTree.PendingFolder!.ParentFolderId.Should().Be(breadsId);
        state.View.List.CancelTreeName();

        // With a stage picked, at the top of that stage.
        state.View.List.SelectedTreeNode = state.View.List.ProductTree.StageFolder(ProductStage.Innovation);
        await RunAsync(state.View.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        state.View.List.ProductTree.PendingFolder!.Should().Match<ProductFolderNode>(f => f.Stage == ProductStage.Innovation && f.ParentFolderId == null);
    });

    [Test]
    public Task Rename_on_a_folder_types_the_name_in_place_and_renames_it_with_nothing_on_the_form_to_save() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await OpenedStateAsync(mamonId);
        var breads = state.View.List.ProductTree.Folder(breadsId)!;
        state.View.List.SelectedTreeNode = breads;

        await RunAsync(state.View.List.TreeShortcut("F2")!);

        state.View.List.TreeNameEdit!.Node.Should().BeSameAs(breads);
        state.View.List.TreeNameEdit.InitialText.Should().Be("Breads");

        await state.View.List.CommitTreeNameAsync("Bread");

        state.ErrorMessage.Should().BeNull();
        (await SingleFolderAsync("Bread")).Id.Should().Be(breadsId);
        state.View.List.ProductTree.Folder(breadsId).Should().BeSameAs(breads);
        breads.Title.Should().Be("Bread");
        breads.Products.Should().ContainSingle();
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task New_Product_Here_names_the_product_in_its_folder_and_saves_it_there_at_once() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var state = await LoadedStateAsync();
        var breads = state.View.List.ProductTree.Folder(breadsId)!;
        state.View.List.SearchText = "nothing like it";

        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));

        state.View.List.SearchText.Should().BeEmpty("the search is cleared, so the new row shows");
        var row = state.View.List.ProductTree.PendingProduct!;
        breads.Children.Should().Equal(row);
        state.View.List.SelectedTreeNode.Should().BeSameAs(row);
        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(row, "New product"));
        state.View.List.TreeMenuFor(row).Should().BeEmpty();
        state.View.List.SelectedTreeProducts.Should().BeEmpty();
        state.SelectedProduct.Should().BeNull();
        state.History.IsDirty.Should().BeFalse();

        await state.LoadCommand.ExecuteAsync();
        breads.Children.Should().Equal([row], "a load keeps it");
        state.View.List.SelectedTreeNode.Should().BeSameAs(row);

        await state.View.List.CommitTreeNameAsync("Pan de Coco");
        await Settle.UntilAsync(state, () => state.AreDetailsReady, () => $"ready={state.AreDetailsReady}");

        state.ErrorMessage.Should().BeNull();
        var saved = await ProductNamedAsync("Pan de Coco");
        (saved.Stage, saved.FolderId, saved.IsComposite).Should().Be((ProductStage.Innovation, breadsId, false));
        await using (var dbContext = CreateDbContext())
        {
            (await dbContext.ProductVersions.CountAsync(v => v.ProductId == saved.Id, Cancellation)).Should().Be(1, "it's saved as Save saves it");
        }

        state.SelectedProduct!.Id.Should().Be(saved.Id, "the form opens on it");
        state.View.List.ProductTree.PendingProduct.Should().BeNull();
        state.View.List.TreeNameEdit.Should().BeNull();
        breads.Children.Should().Equal(state.View.List.ProductTree.Find(saved.Id)!);
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.ProductTree.Find(saved.Id));
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_new_composite_named_in_the_list_keeps_its_row_and_the_name_on_the_form_until_it_can_be_saved() => UiThread.RunAsync(async () =>
    {
        var state = await LoadedStateAsync();
        var production = state.View.List.ProductTree.StageFolder(ProductStage.Production);

        await state.NewCompositeCommand.ExecuteAsync();

        var row = state.View.List.ProductTree.PendingProduct!;
        production.Children.Should().Equal(row);
        row.Product.IsComposite.Should().BeTrue();
        state.IsEditingComposite.Should().BeTrue();

        await state.View.List.CommitTreeNameAsync("Pasalubong Box");

        // It needs two products before it can be saved: the name is the form's, unsaved, and the row keeps it.
        state.ErrorMessage.Should().BeNull();
        state.SuccessMessage.Should().Contain("Products table");
        state.Products.Should().BeEmpty();
        state.View.List.TreeNameEdit.Should().BeNull();
        state.View.List.ProductTree.PendingProduct.Should().BeSameAs(row);
        row.Title.Should().Be("Pasalubong Box");
        state.View.List.SelectedTreeNode.Should().BeSameAs(row);
        state.EditingName.Should().Be("Pasalubong Box");
        state.History.IsDirty.Should().BeTrue("Save is offered for it");

        state.ConfirmDelete.RegisterHandler(interaction => interaction.SetOutput(true));
        await state.RevertCommand.ExecuteAsync();

        state.View.List.ProductTree.PendingProduct.Should().BeNull("put back blank, it has no row");
        production.Children.Should().BeEmpty();
    });

    [Test]
    public Task Esc_or_an_empty_name_takes_a_new_row_away_and_saves_nothing() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var state = await LoadedStateAsync();
        var breads = state.View.List.ProductTree.Folder(breadsId)!;
        var innovation = state.View.List.ProductTree.StageFolder(ProductStage.Innovation);

        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));
        state.View.List.CancelTreeName();

        breads.Children.Should().BeEmpty();
        state.View.List.ProductTree.PendingProduct.Should().BeNull();
        state.View.List.TreeNameEdit.Should().BeNull();
        state.View.List.SelectedTreeNode.Should().BeNull();
        state.SelectedProduct.Should().BeNull("the form stays on the blank new product");

        await RunAsync(state.View.List.TreeMenuFor(breads).Single(i => i.Header == "New Product Here"));
        await state.View.List.CommitTreeNameAsync("  ");
        breads.Children.Should().BeEmpty();

        await RunAsync(state.View.List.TreeMenuFor(innovation).Single(i => i.Header == "New Folder"));
        state.View.List.CancelTreeName();
        TitlesIn(innovation).Should().Equal("Breads");
        state.View.List.ProductTree.PendingFolder.Should().BeNull();
        state.View.List.SelectedTreeNode.Should().BeNull();

        await RunAsync(state.View.List.TreeMenuFor(innovation).Single(i => i.Header == "New Folder"));
        await state.View.List.CommitTreeNameAsync(string.Empty);
        TitlesIn(innovation).Should().Equal("Breads");

        state.ErrorMessage.Should().BeNull();
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
        var state = await OpenedStateAsync(mamonId);
        string? question = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Folder(breadsId)).Single(i => i.Header == "Delete"));

        state.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Breads' folder? Its 1 product and 1 folder move up to Innovation.");
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, (int?)null));
        (await PlaceOfAsync(bibingkaId)).Should().Be((ProductStage.Innovation, holidayId));
        state.View.List.ProductTree.Folder(breadsId).Should().BeNull();
        TitlesIn(state.View.List.ProductTree.StageFolder(ProductStage.Innovation)).Should().Equal("Holiday", "Mamon");
        state.SelectedProduct!.Id.Should().Be(mamonId);
        state.History.IsDirty.Should().BeFalse("the folder went in the database: there's nothing to save");
    });

    [Test]
    public Task Delete_on_a_product_asks_first_moves_it_to_Deleted_and_closes_its_tab_only_if_one_holds_it() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var ensaymadaId = await SaveProductAsync("Ensaymada", null);
        var state = await OpenedStateAsync(mamonId);
        string? question = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(ensaymadaId)).Single(i => i.Header == "Delete"));

        question.Should().Be("Delete 'Ensaymada'? It moves to the Deleted folder at the bottom of the list, where it can be restored as it is.");
        state.Products.Select(p => p.Name).Should().Equal("Mamon");
        state.DeletedProducts.Select(p => p.Name).Should().Equal("Ensaymada");
        TitlesIn(state.View.List.ProductTree.DeletedFolder).Should().Equal("Ensaymada");
        state.SelectedProduct!.Id.Should().Be(mamonId, "the one showing wasn't the one deleted");
        state.View.Tabs.All.Should().ContainSingle("no tab held the one deleted");

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header == "Delete"));

        state.View.Products.Should().BeEmpty();
        TitlesIn(state.View.List.ProductTree.DeletedFolder).Should().Equal("Ensaymada", "Mamon");
        state.View.Tabs.All.Should().BeEmpty("the tab holding the one deleted closed, as it's gone");
        state.View.Form.Should().BeNull();
    });

    [Test]
    public Task A_deleted_products_row_leaves_the_form_as_it_is_and_its_menu_restores_it_or_deletes_it_permanently() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var mamonId = await SaveProductAsync("Mamon", breadsId, ProductStage.Production);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(mamonId, Cancellation);
        var state = await OpenedStateAsync(pandesalId);
        var mamon = state.View.List.ProductTree.Find(mamonId)!;

        state.View.List.SelectedTreeNode = mamon;

        state.SelectedProduct!.Id.Should().Be(pandesalId, "a deleted product isn't opened");
        state.View.List.SelectedTreeNode.Should().BeSameAs(mamon, "and its row stays picked");
        state.View.List.SelectedTreeProducts.Should().BeEmpty("no live product's menu takes it in");
        var menu = state.View.List.TreeMenuFor(mamon);
        Headers(menu).Should().Equal("Restore", "Delete Permanently");
        state.View.List.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == state.View.List.DeletePermanentlyCommand && i.Parameter == mamon);
        state.View.List.TreeShortcut("F2").Should().BeNull();
        state.View.List.TreeMenuFor(state.View.List.ProductTree.DeletedFolder).Should().BeEmpty("the folder itself has nothing to offer");
        state.ComponentsTable.ComponentProductOptions.Select(p => p.Id).Should().NotContain(mamonId);

        await RunAsync(menu.Single(i => i.Header == "Restore"));

        state.ErrorMessage.Should().BeNull();
        state.DeletedProducts.Should().BeEmpty();
        state.View.List.ProductTree.Folder(breadsId)!.Products.Should().ContainSingle(l => l.Product.Id == mamonId, "back where it was");
        state.View.List.ProductTree.Find(mamonId).Should().BeSameAs(mamon, "its row moves back with it");
        state.SelectedProduct!.Id.Should().Be(mamonId, "a restored product is opened");

        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(mamonId, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        string? question = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header == "Delete Permanently"));

        question.Should().StartWith("Delete 'Mamon' permanently?").And.EndWith("This can't be undone.");
        state.DeletedProducts.Should().BeEmpty();
        state.View.List.ProductTree.Find(mamonId).Should().BeNull();
        (await new ProductRepository(CreateDbContextFactory()).GetByIdAsync(mamonId, Cancellation)).Should().BeNull();
    });

    [Test]
    public Task A_deleted_product_opened_from_elsewhere_is_picked_in_the_Deleted_folder_and_a_search_finds_it_there() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        await new ProductRepository(CreateDbContextFactory()).DeleteAsync(mamonId, Cancellation);
        var state = await OpenedStateAsync(pandesalId);
        state.View.List.ProductTree.DeletedFolder.IsExpanded.Should().BeFalse();

        state.OpenRecord(mamonId);

        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.ProductTree.Find(mamonId));
        state.View.List.ProductTree.DeletedFolder.IsExpanded.Should().BeTrue("the row picked shows");
        state.SelectedProduct!.Id.Should().Be(pandesalId, "the form stays on what it showed");

        state.View.List.SearchText = "Pan";
        TitlesIn(state.View.List.ProductTree.DeletedFolder).Should().BeEmpty();
        state.View.List.SearchText = "Mam";
        TitlesIn(state.View.List.ProductTree.DeletedFolder).Should().Equal("Mamon");
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
        var state = await LoadedStateAsync();
        var tree = state.View.List.ProductTree;

        state.View.List.CategoryFilters.Select(f => f.Label).Should().Equal("All categories", "Breads", "Cakes", "Uncategorized");
        state.View.List.CategoryFilter.Should().Be(new ProductCategoryFilter.All());

        state.View.List.CategoryFilter = state.View.List.CategoryFilters.Single(f => f.Label == "Breads");

        state.View.List.VisibleProducts.Select(p => p.Id).Should().BeEquivalentTo([pandesalId, testBunId]);
        TitlesIn(tree.StageFolder(ProductStage.Production)).Should().Equal(["Bakery"], "only the folders holding a match show");
        TitlesIn(tree.Folder(bakeryId)!).Should().Equal("Pandesal");
        TitlesIn(tree.Folder(trialsId)!).Should().Equal("Test Bun");
        TitlesIn(tree.DeletedFolder).Should().Equal(["Old Bun"], "the Deleted folder is filtered too");
        tree.Folder(bakeryId)!.Count.Should().Be(1);

        state.View.List.SearchText = "test";
        state.View.List.VisibleProducts.Select(p => p.Id).Should().Equal([testBunId], "with a search, both have to match");
        state.View.List.SearchText = string.Empty;

        state.View.List.CategoryFilter = new ProductCategoryFilter.Uncategorized();
        state.View.List.VisibleProducts.Select(p => p.Id).Should().Equal(looseId);
        TitlesIn(tree.StageFolder(ProductStage.Production)).Should().Equal("Loose Loaf");

        // A product opened from elsewhere that the filter hides shows: the filter goes.
        state.OpenRecord(bibingkaId);
        state.View.List.CategoryFilter.Should().Be(new ProductCategoryFilter.All());
        state.View.List.SelectedTreeNode.Should().BeSameAs(tree.Find(bibingkaId));

        // Renamed in Settings, the category keeps its place picked; deleted, the filter goes back to All categories.
        state.View.List.CategoryFilter = state.View.List.CategoryFilters.Single(f => f.Label == "Cakes");
        await new ProductCategoryRepository(CreateDbContextFactory()).UpdateAsync(new ProductCategory { Id = cakesCategory, Name = "Cakes & Tortes" }, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.View.List.CategoryFilter.Should().Be(new ProductCategoryFilter.Category(cakesCategory, "Cakes & Tortes"));
        state.View.List.VisibleProducts.Select(p => p.Id).Should().Equal(bibingkaId);

        await new ProductCategoryRepository(CreateDbContextFactory()).DeleteAsync(cakesCategory, Cancellation);
        await state.LoadCommand.ExecuteAsync();
        state.View.List.CategoryFilter.Should().Be(new ProductCategoryFilter.All());
        state.View.List.CategoryFilters.Select(f => f.Label).Should().Equal("All categories", "Breads", "Uncategorized");

        // New Folder clears the filters, so its row shows.
        state.View.List.CategoryFilter = state.View.List.CategoryFilters.Single(f => f.Label == "Breads");
        await RunAsync(state.View.List.TreeMenuFor(tree.StageFolder(ProductStage.Production)).Single(i => i.Header == "New Folder"));
        state.View.List.CategoryFilter.Should().Be(new ProductCategoryFilter.All());
        TitlesIn(tree.StageFolder(ProductStage.Production)).Should().Contain("New folder");
    });

    [Test]
    public Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var state = await LoadedStateAsync();
        var mamon = state.View.List.ProductTree.Find(mamonId)!;
        var breads = state.View.List.ProductTree.Folder(breadsId)!;

        state.View.List.SelectedTreeNode = mamon;
        state.View.List.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == state.View.List.RenameProductCommand && i.Parameter == mamon);
        state.View.List.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == state.View.List.DeleteProductCommand && i.Parameter == mamon);

        state.View.List.SelectedTreeNode = breads;
        state.View.List.TreeShortcut("F2")!.Command.Should().BeSameAs(state.View.List.RenameFolderCommand);
        state.View.List.TreeShortcut("Del")!.Command.Should().BeSameAs(state.View.List.DeleteFolderCommand);

        state.View.List.SelectedTreeNode = state.View.List.ProductTree.StageFolder(ProductStage.Production);
        state.View.List.TreeShortcut("F2").Should().BeNull("a stage can't be renamed");
        state.View.List.TreeShortcut("Del").Should().BeNull("nor deleted");

        state.View.List.SelectedTreeNode = mamon;
        state.View.List.SelectedTreeNodes.Add(state.View.List.ProductTree.Find(pandesalId)!);
        state.View.List.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    });

    [Test]
    public Task A_product_opened_from_elsewhere_is_picked_alone_in_the_list() => UiThread.RunAsync(async () =>
    {
        var mamonId = await SaveProductAsync("Mamon", null);
        var pandesalId = await SaveProductAsync("Pandesal", null);
        var ensaymadaId = await SaveProductAsync("Ensaymada", null);
        var state = await OpenedStateAsync(mamonId);
        state.View.List.SelectedTreeNodes.Add(state.View.List.ProductTree.Find(pandesalId)!);

        state.OpenRecord(ensaymadaId);

        state.View.List.SelectedTreeNodes.Should().Equal(state.View.List.ProductTree.Find(ensaymadaId)!);
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.ProductTree.Find(ensaymadaId));
    });

    [Test]
    public Task Undoing_a_move_back_into_a_folder_deleted_since_is_refused_and_says_why() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var cakesId = await AddFolderAsync("Cakes");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await LoadedStateAsync();
        var moveTo = state.View.List.TreeMenuFor(state.View.List.ProductTree.Find(mamonId)).Single(i => i.Header == "Move to");
        await RunAsync(moveTo.Items.Single(i => i.Header == "Innovation").Items.Single(i => i.Header == "Cakes"));
        await new ProductFolderRepository(CreateDbContextFactory()).DeleteAsync(breadsId, Cancellation);

        await state.View.List.UndoMoveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("The folder to move it to no longer exists.");
        (await PlaceOfAsync(mamonId)).Should().Be((ProductStage.Innovation, cakesId));
    });

    [Test]
    public Task The_folders_left_closed_stay_closed_next_time_and_a_new_folder_opens() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var store = new InMemoryViewStateStore();
        (await LoadedStateAsync(store)).View.Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedStateAsync(store);

        first.View.List.ProductTree.Folder(breadsId)!.IsExpanded = false;

        await Settle.EventuallyAsync(
            () => store.ReadList(ProductsList.ClosedFoldersKey)?.Contains($"Folder/{breadsId}") == true,
            () => string.Join(", ", store.ReadList(ProductsList.ClosedFoldersKey) ?? []));
        first.View.Dispose();

        var pastriesId = await AddFolderAsync("Pastries", ProductStage.Production);
        var next = await LoadedStateAsync(store);
        next.View.List.ProductTree.Folder(breadsId)!.IsExpanded.Should().BeFalse();
        next.View.List.ProductTree.Folder(cakesId)!.IsExpanded.Should().BeTrue();
        next.View.List.ProductTree.Folder(pastriesId)!.IsExpanded.Should().BeTrue("a folder new since opens, as every folder first did");

        // One closed just before the app closes is written as it closes.
        next.View.List.ProductTree.Folder(cakesId)!.IsExpanded = false;
        next.View.Dispose();
        store.ReadList(ProductsList.ClosedFoldersKey).Should().Contain($"Folder/{cakesId}");
    });

    [Test]
    public Task The_folders_the_list_kept_open_before_open_again_and_the_rest_start_closed() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads", ProductStage.Production);
        var cakesId = await AddFolderAsync("Cakes", ProductStage.Production);
        var store = new InMemoryViewStateStore();
        store.WriteList(ProductsList.OpenFoldersKey, ["Production", $"Folder/{breadsId}"]);

        var state = await LoadedStateAsync(store);

        state.View.List.ProductTree.Folder(breadsId)!.IsExpanded.Should().BeTrue();
        state.View.List.ProductTree.Folder(cakesId)!.IsExpanded.Should().BeFalse();
        state.View.List.ProductTree.StageFolder(ProductStage.Innovation).IsExpanded.Should().BeFalse();

        // Kept as closed folders from then on.
        state.View.Dispose();
        store.ReadList(ProductsList.ClosedFoldersKey).Should().Contain($"Folder/{cakesId}").And.NotContain($"Folder/{breadsId}");
    });

    [Test]
    public Task Innovation_left_closed_under_its_former_name_stays_closed() => UiThread.RunAsync(async () =>
    {
        await AddFolderAsync("Breads");
        var store = new InMemoryViewStateStore();
        store.WriteList(ProductsList.ClosedFoldersKey, ["Deleted", "ResearchAndDevelopment"]);

        var state = await LoadedStateAsync(store);

        state.View.List.ProductTree.StageFolder(ProductStage.Innovation).IsExpanded.Should().BeFalse("it was left closed before the rename");

        // Kept by its key now once a folder is opened or closed.
        state.View.List.ProductTree.StageFolder(ProductStage.Production).IsExpanded = false;
        state.View.Dispose();
        store.ReadList(ProductsList.ClosedFoldersKey).Should().Contain("Innovation").And.NotContain("ResearchAndDevelopment");
    });

    private static IEnumerable<string> Headers(IEnumerable<TreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    private static IEnumerable<string> TitlesIn(ProductFolderNode folder) => folder.Children.Select(c => c.Title);

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

        var state = await LoadedStateAsync();
        await Settle.UntilAsync(state, () => state.View.List.ProductTree.Find(shortId)!.IsBelowTargetMarkup, () => "not flagged yet");

        state.View.List.ProductTree.Find(enoughId)!.IsBelowTargetMarkup.Should().BeFalse();
    });

    [Test]
    public Task A_product_with_a_section_that_has_no_piece_size_is_flagged_in_the_list() => UiThread.RunAsync(async () =>
    {
        var fillingId = await AddRoleAsync("Filling");
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.25m);

        // Ube Pandesal's filling has neither a weight per piece nor a yield yet, so it costs the bread nothing.
        var (unsizedId, _) = await AddProductWithSubAssemblyAsync("Ube Pandesal", fillingId, [(ubeId, 100m)], weightPerPiece: 0m);
        var (sizedId, _) = await AddProductWithSubAssemblyAsync("Ube Roll", fillingId, [(ubeId, 100m)], weightPerPiece: 10m);

        var state = await LoadedStateAsync();
        await Settle.UntilAsync(state, () => state.View.List.ProductTree.Find(unsizedId)!.HasSectionWithoutPieceSize, () => "not flagged yet");

        state.View.List.ProductTree.Find(sizedId)!.HasSectionWithoutPieceSize.Should().BeFalse();
    });

    private async Task PriceAsync(int productId, decimal currentPrice, decimal targetMarkupPercent)
    {
        await using var dbContext = CreateDbContext();
        var product = await dbContext.Products.SingleAsync(p => p.Id == productId, Cancellation);
        product.CurrentPrice = currentPrice;
        if (await dbContext.CostingParameters.SingleOrDefaultAsync(c => c.ProductId == productId, Cancellation) is { } costing)
        {
            costing.MarkupPercent = targetMarkupPercent;
        }
        else
        {
            dbContext.CostingParameters.Add(new CostingParameters { ProductId = productId, MarkupPercent = targetMarkupPercent });
        }

        await dbContext.SaveChangesAsync(Cancellation);
    }

    [Test]
    public Task Picking_another_product_over_unsaved_changes_opens_it_in_a_tab_of_its_own_and_asks_nothing() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var state = await OpenedStateAsync(mamonId);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        state.EditingName = "Mamon Tostado";

        Click(state, state.View.List.ProductTree.Find(pandesalId)!);
        var pandesal = state.View.Form!;
        await Settle.UntilAsync(pandesal, () => pandesal.SelectedProduct?.Id == pandesalId && pandesal.AreDetailsReady, () => $"selected={pandesal.SelectedProduct?.Id}");

        asked.Should().BeEmpty("the changes stay in their own tab: nothing is left");
        pandesal.Should().NotBeSameAs(state);
        pandesal.EditingName.Should().Be("Pandesal");
        state.SelectedProduct!.Id.Should().Be(mamonId);
        state.EditingName.Should().Be("Mamon Tostado");
        state.History.IsDirty.Should().BeTrue();
        state.View.Tabs.All.Select(tab => tab.Form).Should().Equal(state, pandesal);
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.ProductTree.Find(pandesalId), "the list follows the tab showing");
    });

    [Test]
    public Task A_pick_takes_the_preview_tabs_place_while_it_holds_nothing_unsaved_and_a_tab_kept_or_edited_stays() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var ensaymadaId = await SaveProductAsync("Ensaymada", breadsId);
        var state = await LoadedStateAsync();
        var view = state.View;

        // The blank form the view opens on is the preview tab: the first pick takes its place, and the next one's.
        Click(state, view.List.ProductTree.Find(mamonId)!);
        Click(state, view.List.ProductTree.Find(pandesalId)!);
        await Settle.UntilAsync(state, () => state.SelectedProduct?.Id == pandesalId && state.AreDetailsReady);
        view.Tabs.All.Should().ContainSingle().Which.IsPreview.Should().BeTrue();

        // Kept by a double-click on its row: the next pick opens a tab of its own.
        view.List.KeepOpen(view.List.ProductTree.Find(pandesalId)!);
        view.Tabs.Active!.IsPreview.Should().BeFalse();
        Click(state, view.List.ProductTree.Find(ensaymadaId)!);
        var ensaymada = view.Form!;
        await Settle.UntilAsync(ensaymada, () => ensaymada.SelectedProduct?.Id == ensaymadaId && ensaymada.AreDetailsReady);
        view.Tabs.All.Select(tab => tab.Form).Should().Equal(state, ensaymada);
        view.Tabs.Preview!.Form.Should().BeSameAs(ensaymada);

        // An edit keeps the preview tab too: the next pick doesn't take its place.
        ensaymada.EditingName = "Ensaymada Special";
        Click(state, view.List.ProductTree.Find(mamonId)!);
        var mamon = view.Form!;
        await Settle.UntilAsync(mamon, () => mamon.SelectedProduct?.Id == mamonId && mamon.AreDetailsReady);
        view.Tabs.All.Select(tab => tab.Form).Should().Equal(state, ensaymada, mamon);
        ensaymada.EditingName.Should().Be("Ensaymada Special");

        // A product open already shows in its own tab.
        Click(state, view.List.ProductTree.Find(pandesalId)!);
        view.Form.Should().BeSameAs(state);
        view.Tabs.All.Should().HaveCount(3);
    });

    [Test]
    [Arguments(UnsavedChangesAnswer.Save, "Mamon Tostado", true)]
    [Arguments(UnsavedChangesAnswer.DontSave, "Mamon", true)]
    [Arguments(UnsavedChangesAnswer.Cancel, "Mamon", false)]
    public Task Closing_a_tab_with_changes_asks_about_them_first(UnsavedChangesAnswer answer, string savedName, bool closes) => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var pandesalId = await SaveProductAsync("Pandesal", breadsId);
        var state = await OpenedStateAsync(mamonId);
        var view = state.View;
        var asked = AnswerUnsavedChanges(state, answer);
        state.EditingName = "Mamon Tostado";
        Click(state, view.List.ProductTree.Find(pandesalId)!);
        var mamonTab = view.Tabs.Holding(mamonId)!;

        (await view.CloseTabAsync(mamonTab)).Should().Be(closes);

        asked.Should().ContainSingle().Which.Should().StartWith("Save the changes to Mamon Tostado?");
        view.Tabs.All.Contains(mamonTab).Should().Be(!closes);
        (await NameOfAsync(mamonId)).Should().Be(savedName);
        if (!closes)
        {
            view.Tabs.Active.Should().BeSameAs(mamonTab, "it's shown, so the question is about the form in view");
            state.EditingName.Should().Be("Mamon Tostado");
        }
    });

    [Test]
    public Task New_over_unsaved_changes_opens_a_new_tab_and_keeps_them() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await OpenedStateAsync(mamonId);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        state.EditingName = "Mamon Tostado";

        await state.NewCommand.ExecuteAsync();

        asked.Should().BeEmpty();
        var fresh = state.View.Form!;
        fresh.Should().NotBeSameAs(state);
        fresh.EditingId.Should().Be(0);
        state.SelectedProduct!.Id.Should().Be(mamonId);
        state.EditingName.Should().Be("Mamon Tostado");
        state.View.List.ProductTree.PendingProduct.Should().NotBeNull("the new product's row is added for its name");
    });

    [Test]
    public Task Clone_over_unsaved_changes_asks_first_and_Cancel_makes_nothing() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await OpenedStateAsync(mamonId);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        state.EditingName = "Mamon Tostado";
        state.EditingCloneName = "Mamon Ube";

        await state.CloneCommand.ExecuteAsync();

        asked.Should().ContainSingle();
        state.Products.Should().NotContain(p => p.Name == "Mamon Ube");
        state.SelectedProduct!.Id.Should().Be(mamonId);
        state.EditingName.Should().Be("Mamon Tostado");
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved() => UiThread.RunAsync(async () =>
    {
        var breadsId = await AddFolderAsync("Breads");
        var mamonId = await SaveProductAsync("Mamon", breadsId);
        var state = await OpenedStateAsync(mamonId);
        AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        state.EditingName = "Mamon Tostado";

        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();

        await Settle.UntilAsync(state,
            () => state.EditingName == "Mamon" && state.AreDetailsReady && !state.History.IsDirty,
            () => $"name={state.EditingName}");
        state.SelectedProduct!.Id.Should().Be(mamonId);
        (await NameOfAsync(mamonId)).Should().Be("Mamon");
    });

    // No puts a new product back blank. What's typed into it next has to count, or it's lost with no question asked.
    [Test]
    public Task What_is_typed_after_no_on_a_new_product_counts_as_unsaved() => UiThread.RunAsync(async () =>
    {
        var state = await LoadedStateAsync();
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        state.EditingName = "Mamon";
        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        state.EditingName.Should().BeEmpty();
        state.History.IsDirty.Should().BeFalse();
        state.History.CanUndo.Should().BeFalse("what was thrown away isn't there to undo back to");

        state.EditingName = "Mamon Tostado";

        state.History.IsDirty.Should().BeTrue();
        await state.ResolveUnsavedChangesAsync(revert: false);
        asked.Should().HaveCount(2);
    });

    /// <summary>Answers the question about unsaved changes with <paramref name="answer"/>, collecting each question asked.</summary>
    private static List<string> AnswerUnsavedChanges(ProductForm state, UnsavedChangesAnswer answer)
    {
        List<string> asked = [];
        state.ConfirmUnsavedChanges.RegisterHandler(context =>
        {
            asked.Add(context.Input);
            context.SetOutput(answer);
        });
        return asked;
    }

    /// <summary>A click on <paramref name="row"/>, as the tree makes one: the rows picked replaced by it.</summary>
    private static void Click(ProductForm state, object row)
    {
        state.View.List.SelectedTreeNodes.Clear();
        state.View.List.SelectedTreeNodes.Add(row);
    }

    private async Task<string> NameOfAsync(int productId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.Products.Where(p => p.Id == productId).Select(p => p.Name).SingleAsync(Cancellation);
    }

    /// <summary>Drops <paramref name="nodes"/> on <paramref name="target"/> as the list does, then waits for the moves to be offered again.</summary>
    private static async Task<object?> DropAsync(ProductForm state, IEnumerable<object> nodes, object? target)
    {
        var shown = await ((IListTreeScreen)state.View.List).DropAsync(nodes, target);
        await MovesOfferedAgainAsync(state);
        return shown;
    }

    /// <summary>
    /// The moves are offered again once a move or its Undo is done, before a drop or a paste: a command's run is over as
    /// its ExecuteAsync ends.
    /// </summary>
    private static Task MovesOfferedAgainAsync(ProductForm state)
    {
        ((System.Windows.Input.ICommand)state.View.List.MoveFolderCommand).CanExecute(null).Should().BeTrue("the move is done");
        return Task.CompletedTask;
    }

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    private static async Task RunAsync(TreeMenuItem item)
    {
        switch (item.Command)
        {
            case ScreenCommand<IReadOnlyList<ProductMove>> move:
                await move.ExecuteAsync((IReadOnlyList<ProductMove>)item.Parameter!);
                break;
            case ScreenCommand<StageMoveRequest> toStage:
                await toStage.ExecuteAsync((StageMoveRequest)item.Parameter!);
                break;
            case ScreenCommand<ProductFolderMove> moveFolder:
                await moveFolder.ExecuteAsync((ProductFolderMove)item.Parameter!);
                break;
            case ScreenCommand<IReadOnlyList<ProductTreeNode>> onRows:
                await onRows.ExecuteAsync((IReadOnlyList<ProductTreeNode>)item.Parameter!);
                break;
            case ScreenCommand<ProductTreeNode> onRow:
                await onRow.ExecuteAsync((ProductTreeNode)item.Parameter!);
                break;
            case ScreenCommand plain:
                await plain.ExecuteAsync();
                break;
            case ScreenCommand<IReadOnlyList<Product>> onProducts:
                await onProducts.ExecuteAsync((IReadOnlyList<Product>)item.Parameter!);
                break;
            case ScreenCommand<ProductLeafNode> onProduct:
                await onProduct.ExecuteAsync((ProductLeafNode)item.Parameter!);
                break;
            case ScreenCommand<ProductFolderNode?> onFolder:
                await onFolder.ExecuteAsync((ProductFolderNode?)item.Parameter);
                break;
            default:
                throw new InvalidOperationException($"'{item.Header}' runs nothing a test knows how to wait for.");
        }
    }

    /// <summary>A name typed into the header and left, as one undo step, the way the name box records it.</summary>
    private static void TypeName(ProductForm state, string name)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.EditingName = name;
        recorder.EndUndoStep();
    }

    private static void TypeDescription(ProductForm state, string description)
    {
        var recorder = (IUndoRecorder)state;
        recorder.BeginUndoStep();
        state.EditingDescription = description;
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

    private async Task<ProductForm> LoadedStateAsync(IViewStateStore? viewStateStore = null)
    {
        var state = CreateState(viewStateStore);
        await state.LoadCommand.ExecuteAsync();
        return state;
    }

    /// <summary>The screen with <paramref name="productId"/> open on the form, its details in.</summary>
    private async Task<ProductForm> OpenedStateAsync(int productId)
    {
        var state = await LoadedStateAsync();
        state.OpenRecord(productId);
        await Settle.UntilAsync(state, () => state.AreDetailsReady && state.SelectedProduct?.Id == productId, () => $"ready={state.AreDetailsReady}");
        return state;
    }

    private ProductForm CreateState(IViewStateStore? viewStateStore)
    {
        var factory = CreateDbContextFactory();
        return ProductsViews.Create(factory, CreateUnitOfWork(), viewStateStore).Form!;
    }
}
