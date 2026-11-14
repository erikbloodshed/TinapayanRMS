using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Windows.Input;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using ReactiveUI;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// The Raw Materials list's right-click menus, its keys and its selection, as ProductsViewModelTreeTests has them for the
/// Products list: what each menu offers a material, several materials, a folder and the list itself, and what each item
/// does, run as the menu runs it. A material only moves between categories of its own type.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class RawMaterialsViewModelTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_materials_menu_moves_it_to_another_category_of_its_type_and_offers_rename_and_delete() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        var leaf = viewModel.List.RawMaterialTree.Find(breadFlourId)!;
        viewModel.List.SelectedTreeNode = leaf;

        var menu = viewModel.List.TreeMenuFor(leaf);

        Headers(menu).Should().Equal("Move to", "Rename", "Copy", "Delete");
        menu.Single(i => i.Header == "Rename").Gesture.Should().Be("F2");
        menu.Single(i => i.Header == "Delete").Gesture.Should().Be("Del");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        moveTo.Select(i => i.Header).Should().Equal(["Dry Goods", "Flour"], "only its own type's categories");
        moveTo.Single(i => i.Header == "Flour").IsEnabled.Should().BeFalse("it's in that folder already");
    });

    [Test]
    public Task A_drop_moves_materials_into_another_category_of_their_type_and_gives_the_first_back_to_bring_into_view() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        IListTreeScreen screen = viewModel.List;
        var breadFlour = viewModel.List.RawMaterialTree.Find(breadFlourId)!;

        screen.CanDrag(breadFlour).Should().BeTrue();
        screen.CanDrag(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour")).Should().BeFalse("a folder isn't dragged");
        screen.CanDrop([breadFlour], TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes")).Should().BeFalse("a material's type is fixed");
        screen.CanDrop([breadFlour], viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().BeFalse("a type's folder has no category to give");

        (await screen.DropAsync([breadFlour], TreeFolder(viewModel, RawMaterialType.Ingredient, "Dry Goods"))).Should().BeSameAs(breadFlour);

        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        viewModel.List.MoveNotice.Should().Be("Moved Bread Flour to Ingredients › Dry Goods");
    });

    [Test]
    public Task A_drop_made_while_a_move_is_still_saving_does_nothing() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var caneSugarId = await AddIngredientAsync("Cane Sugar", flourId);
        var viewModel = await LoadedViewModelAsync();
        IListTreeScreen screen = viewModel.List;

        var firstMove = viewModel.List.MoveMaterialsCommand.Execute([new RawMaterialMove(breadFlourId, dryGoodsId)]).ToTask();
        // As a drop would find it: the move under way (ReactiveCommand says so on its scheduler, a moment after Execute).
        await viewModel.List.MoveMaterialsCommand.CanExecute.FirstAsync(offered => !offered);
        (await screen.DropAsync([viewModel.List.RawMaterialTree.Find(caneSugarId)!], TreeFolder(viewModel, RawMaterialType.Ingredient, "Dry Goods")))
            .Should().BeNull("a move is still saving");
        await firstMove;

        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        (await CategoryOfAsync(caneSugarId)).Should().Be(flourId, "the drop made meanwhile did nothing");
    });

    [Test]
    public Task Move_to_saves_at_once_says_so_and_Undo_on_the_notice_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(MoveToItem(viewModel, breadFlourId, "Dry Goods"));

        viewModel.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Dry Goods").Materials.Should().ContainSingle();
        viewModel.List.MoveNotice.Should().Be("Moved Bread Flour to Ingredients › Dry Goods");

        await viewModel.List.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(flourId);
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").Materials.Should().ContainSingle();
        viewModel.List.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        (await viewModel.List.UndoMoveCommand.CanExecute.FirstAsync()).Should().BeFalse();
    });

    [Test]
    public Task Several_materials_picked_move_together_and_the_one_showing_stays_on_the_form() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var sugarId = await AddIngredientAsync("Sugar", dryGoodsId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        var tree = viewModel.List.RawMaterialTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        viewModel.List.SelectedTreeNodes.Add(tree.Find(cakeFlourId)!);
        viewModel.List.SelectedTreeNodes.Add(tree.Find(sugarId)!);
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);

        var menu = viewModel.List.TreeMenuFor(tree.Find(cakeFlourId));

        Headers(menu).Should().Equal(["Move 3 Materials to"], "renaming or deleting is for one at a time");
        await RunAsync(menu[0].Items.Single(i => i.Header == "Dry Goods"));

        viewModel.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        (await CategoryOfAsync(cakeFlourId)).Should().Be(dryGoodsId);
        viewModel.List.MoveNotice.Should().Be("Moved 2 materials to Ingredients › Dry Goods", "Sugar was there already");
        viewModel.List.SelectedTreeMaterials.Select(l => l.RawMaterial.Id).Should().Equal(breadFlourId, cakeFlourId, sugarId);
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
    });

    [Test]
    public Task Ingredients_and_packaging_picked_together_have_nowhere_to_move() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeBoxId = await AddPackagingAsync("Cake Box", boxesId);
        var viewModel = await LoadedViewModelAsync();
        viewModel.List.SelectedTreeNode = viewModel.List.RawMaterialTree.Find(breadFlourId);
        viewModel.List.SelectedTreeNodes.Add(viewModel.List.RawMaterialTree.Find(cakeBoxId)!);

        viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(cakeBoxId)).Should().BeEmpty();
    });

    [Test]
    public Task A_move_of_the_material_showing_moves_its_Category_picker_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        Edit(viewModel, () => viewModel.PurchaseCost.EditingCostBufferPercent = 5m);

        await RunAsync(MoveToItem(viewModel, breadFlourId, "Dry Goods"));

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingCategory!.Id.Should().Be(dryGoodsId);
        viewModel.PurchaseCost.EditingCostBufferPercent.Should().Be(5m);
        viewModel.History.IsDirty.Should().BeTrue("the buffer is still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.PurchaseCost.EditingCostBufferPercent.Should().Be(0m);
        viewModel.EditingCategory!.Id.Should().Be(dryGoodsId, "no undo takes back a move that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Rename_types_the_name_in_place_saves_it_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        Edit(viewModel, () => viewModel.PurchaseCost.EditingCostBufferPercent = 5m);
        var breadFlour = viewModel.List.RawMaterialTree.Find(breadFlourId)!;

        await RunAsync(viewModel.List.TreeMenuFor(breadFlour).Single(i => i.Header == "Rename"));

        viewModel.List.TreeNameEdit.Should().Be(new TreeNameEdit(breadFlour, "Bread Flour"));

        await viewModel.List.CommitTreeNameAsync(" Strong Flour ");
        await viewModel.List.CommitTreeNameAsync("Strong Flour Again");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.TreeNameEdit.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.RawMaterials.SingleAsync(m => m.Id == breadFlourId, Cancellation);
            (saved.Name, saved.CostBufferPercent).Should().Be(("Strong Flour", 0m), "the rename saves the name alone, once");
        }

        viewModel.EditingName.Should().Be("Strong Flour");
        viewModel.List.RawMaterialTree.Find(breadFlourId)!.Title.Should().Be("Strong Flour");
        viewModel.History.IsDirty.Should().BeTrue("the buffer is still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingName.Should().Be("Strong Flour", "no undo takes back a rename that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_rename_to_a_name_in_use_is_refused_says_why_and_brings_the_box_back() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        var breadFlour = viewModel.List.RawMaterialTree.Find(breadFlourId)!;

        await RunAsync(viewModel.List.TreeMenuFor(breadFlour).Single(i => i.Header == "Rename"));
        await viewModel.List.CommitTreeNameAsync("Cake Flour");

        viewModel.ErrorMessage.Should().Be("That name is already in use.");
        viewModel.List.RawMaterialTree.Find(breadFlourId)!.Title.Should().Be("Bread Flour");
        viewModel.List.TreeNameEdit.Should().Be(new TreeNameEdit(breadFlour, "Cake Flour"));

        // Esc, an empty name and the same name leave it be; so does a search.
        viewModel.List.CancelTreeName();
        viewModel.List.TreeNameEdit.Should().BeNull();
        viewModel.List.SelectedTreeNode = breadFlour;
        await RunAsync(viewModel.List.TreeShortcut("F2")!);
        await viewModel.List.CommitTreeNameAsync("  ");
        await RunAsync(viewModel.List.TreeShortcut("F2")!);
        await viewModel.List.CommitTreeNameAsync("Bread Flour");
        await RunAsync(viewModel.List.TreeShortcut("F2")!);
        viewModel.List.SearchText = "flour";
        viewModel.List.TreeNameEdit.Should().BeNull();
        (await NameOfAsync(breadFlourId)).Should().Be("Bread Flour");
    });

    [Test]
    public Task New_names_the_material_in_its_folder_and_the_row_stays_until_Save_files_it() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var viewModel = await LoadedViewModelAsync();
        var flour = TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour");
        viewModel.List.SearchText = "nothing like it";

        await RunAsync(viewModel.List.TreeMenuFor(flour).Single(i => i.Header == "New Ingredient Here"));

        viewModel.List.SearchText.Should().BeEmpty("the search is cleared, so the new row shows");
        var row = viewModel.List.RawMaterialTree.PendingMaterial!;
        flour.Children.Should().Equal(row);
        flour.MaterialCount.Should().Be(0, "it isn't a material until it's saved");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(row);
        viewModel.List.TreeNameEdit.Should().Be(new TreeNameEdit(row, "New ingredient"));
        viewModel.List.TreeMenuFor(row).Should().BeEmpty();
        viewModel.List.SelectedTreeMaterials.Should().BeEmpty();
        viewModel.EditingCategory!.Id.Should().Be(flourId);

        await viewModel.List.CommitTreeNameAsync("Rye Flour");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.RawMaterials.Should().BeEmpty("a material needs more than a name, so Save files it");
        viewModel.List.TreeNameEdit.Should().BeNull();
        row.Title.Should().Be("Rye Flour");
        viewModel.EditingName.Should().Be("Rye Flour");
        viewModel.History.IsDirty.Should().BeTrue("Save is offered for it");

        await viewModel.LoadCommand.Execute();
        flour.Children.Should().Equal([row], "a load keeps it");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(row);

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.RawMaterialTree.PendingMaterial.Should().BeNull();
        var saved = viewModel.List.RawMaterialTree.Find(viewModel.SelectedRawMaterial!.Id)!;
        flour.Children.Should().Equal(saved);
        saved.Title.Should().Be("Rye Flour");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(saved);
    });

    [Test]
    public Task New_from_the_header_names_an_ingredient_in_the_Ingredients_folder_and_Esc_or_Dont_Save_takes_the_row_away() => UiThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var viewModel = await LoadedViewModelAsync();
        AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        var ingredients = viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);

        await viewModel.NewCommand.Execute();

        var row = viewModel.List.RawMaterialTree.PendingMaterial!;
        TitlesIn(ingredients).Should().Equal(["Flour", "New ingredient"], "with no category yet, it's last in its type's folder");
        viewModel.List.CancelTreeName();
        TitlesIn(ingredients).Should().Equal("Flour");
        viewModel.List.SelectedTreeNode.Should().BeNull();
        viewModel.SelectedRawMaterial.Should().BeNull("the form stays on the blank new material");

        await viewModel.NewCommand.Execute();
        await viewModel.List.CommitTreeNameAsync("Rye Flour");
        TitlesIn(ingredients).Should().Equal("Flour", "Rye Flour");

        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("Don't Save puts the form back blank");

        viewModel.List.RawMaterialTree.PendingMaterial.Should().BeNull("put back blank, it has no row");
        TitlesIn(ingredients).Should().Equal("Flour");
        viewModel.RawMaterials.Should().BeEmpty();
    });

    [Test]
    public Task New_Here_starts_a_material_of_the_folders_type_in_its_category_that_Save_files_there() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(viewModel.List.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes")).Single(i => i.Header == "New Packaging Material Here"));

        viewModel.SelectedRawMaterial.Should().BeNull();
        viewModel.EditingType.Should().Be(RawMaterialType.Packaging);
        viewModel.EditingCategory!.Id.Should().Be(boxesId);
        viewModel.History.IsDirty.Should().BeFalse("nothing is typed yet");

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Single(i => i.Header == "New Ingredient Here"));

        viewModel.EditingType.Should().Be(RawMaterialType.Ingredient);
        viewModel.EditingCategory.Should().BeNull("a type's folder has no category");

        await RunAsync(viewModel.List.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour")).Single(i => i.Header == "New Ingredient Here"));
        Edit(viewModel, () => viewModel.EditingName = "Rye Flour");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").Materials.Select(l => l.Title).Should().Equal("Rye Flour");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.RawMaterialTree.Find(viewModel.SelectedRawMaterial!.Id));
        (await CategoryOfAsync(viewModel.SelectedRawMaterial.Id)).Should().Be(flourId);
    });

    [Test]
    public Task Copy_starts_a_new_material_with_only_the_type_category_and_base_unit_that_Save_adds() => UiThread.RunAsync(async () =>
    {
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var spoonId = await AddPackagingAsync("Disposable Spoon", boxesId);
        var viewModel = await OpenedViewModelAsync(spoonId);
        Edit(viewModel, () => viewModel.PurchaseCost.EditingCostBufferPercent = 25m);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(spoonId)).Single(i => i.Header == "Copy"));

        asked.Should().HaveCount(1, "the spoon's unsaved buffer is asked about first");
        viewModel.SelectedRawMaterial.Should().BeNull("the copy is a new material");
        viewModel.EditingName.Should().Be("Disposable Spoon Copy");
        viewModel.EditingType.Should().Be(RawMaterialType.Packaging);
        viewModel.EditingCategory!.Id.Should().Be(boxesId);
        viewModel.EditingBaseUnit!.Name.Should().Be("piece");
        viewModel.PurchaseCost.EditingCostBufferPercent.Should().Be(10m, "the buffer is the new material's default, not the spoon's");
        viewModel.PurchaseCost.BrandPriceRows.Should().BeEmpty();
        viewModel.CanChangeTypeAndSource.Should().BeTrue();
        viewModel.History.IsDirty.Should().BeTrue("the copy is there to be saved");

        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").Materials.Select(l => l.Title).Should().Equal("Disposable Spoon", "Disposable Spoon Copy");
        await using var dbContext = CreateDbContext();
        var copy = await dbContext.RawMaterials.SingleAsync(m => m.Id == viewModel.SelectedRawMaterial!.Id, Cancellation);
        (copy.Type, copy.CategoryId, copy.BaseUnitId).Should().Be((RawMaterialType.Packaging, boxesId, viewModel.List.RawMaterialTree.Find(spoonId)!.RawMaterial.BaseUnitId));
    });

    [Test]
    public Task Copy_names_past_a_copy_already_there_and_keeps_a_made_ingredient_made() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var inHouseId = await AddCategoryAsync("In-House", RawMaterialCategoryType.Made);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        await AddIngredientAsync("bread flour copy", flourId);
        var syrupId = await AddIngredientAsync("Simple Syrup", inHouseId, RawMaterialSource.Made);
        var viewModel = await LoadedViewModelAsync();
        AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Copy"));

        viewModel.EditingName.Should().Be("Bread Flour Copy 2", "names are told apart without their case");
        (viewModel.EditingType, viewModel.EditingSource).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Purchased));
        viewModel.EditingCategory!.Id.Should().Be(flourId);
        viewModel.EditingBaseUnit.Should().BeNull("an ingredient's base unit is always the gram");

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(syrupId)).Single(i => i.Header == "Copy"));

        viewModel.EditingName.Should().Be("Simple Syrup Copy");
        (viewModel.EditingType, viewModel.EditingSource).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Made));
        viewModel.EditingCategory!.Id.Should().Be(inHouseId);
        viewModel.Recipe.SubAssembly.Should().BeNull("its recipe isn't copied");
    });

    [Test]
    public Task The_type_and_list_menus_add_materials_and_categories_and_open_or_close_every_folder() => UiThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var viewModel = await LoadedViewModelAsync();
        var ingredients = viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);

        Headers(viewModel.List.TreeMenuFor(ingredients)).Should().Equal("New Ingredient Here", "New Folder", "New Made Folder", "Expand All", "Collapse All");
        Headers(viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Packaging)))
            .Should().Equal("New Packaging Material Here", "New Folder", "Expand All", "Collapse All");
        var listMenu = viewModel.List.TreeMenuFor(null);
        Headers(listMenu).Should().Equal(
            "New Raw Material", "New Ingredient Folder", "New Made Folder", "New Packaging Folder", "Expand All", "Collapse All");

        await RunAsync(listMenu.Single(i => i.Header == "Collapse All"));
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse();
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse();
        viewModel.List.RawMaterialTree.Roots.Should().OnlyContain(r => r.IsExpanded);

        await RunAsync(viewModel.List.TreeMenuFor(ingredients).Single(i => i.Header == "Expand All"));
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue();
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse("only that type's folders");

        await RunAsync(listMenu.Single(i => i.Header == "Expand All"));
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeTrue();
    });

    [Test]
    public Task New_Folder_names_a_category_in_place_in_its_type_and_picks_it() => UiThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var viewModel = await LoadedViewModelAsync();
        var ingredients = viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);

        await RunAsync(viewModel.List.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour")).Single(i => i.Header == "New Folder"));

        var folder = viewModel.List.RawMaterialTree.PendingFolder!;
        TitlesIn(ingredients).Should().Equal("Flour", "New folder");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(folder);
        viewModel.List.TreeNameEdit!.Node.Should().BeSameAs(folder);
        viewModel.List.TreeNameEdit.Hint.Should().Be("A folder in Ingredients.");
        viewModel.List.TreeMenuFor(folder).Should().BeEmpty();

        await viewModel.List.CommitTreeNameAsync("Dairy");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.RawMaterialTree.PendingFolder.Should().BeNull();
        viewModel.Categories.Should().ContainSingle(c => c.Name == "Dairy" && c.Type == RawMaterialCategoryType.Ingredient);
        viewModel.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Ingredient, "Dairy"));

        await RunAsync(viewModel.List.TreeMenuFor(null).Single(i => i.Header == "New Packaging Folder"));
        await viewModel.List.CommitTreeNameAsync("Labels");

        viewModel.Categories.Should().ContainSingle(c => c.Name == "Labels" && c.Type == RawMaterialCategoryType.Packaging);
        viewModel.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Packaging, "Labels"));

        await RunAsync(viewModel.List.TreeMenuFor(ingredients).Single(i => i.Header == "New Folder"));
        await viewModel.List.CommitTreeNameAsync("flour");

        viewModel.Categories.Should().HaveCount(3, "a name that's one of the type's categories already adds none");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour"));

        await RunAsync(viewModel.List.TreeMenuFor(null).Single(i => i.Header == "New Made Folder"));
        viewModel.List.TreeNameEdit!.Hint.Should().Be("A folder in Ingredients, for made ingredients only.");
        await viewModel.List.CommitTreeNameAsync("In-House");

        viewModel.Categories.Should().ContainSingle(c => c.Name == "In-House" && c.Type == RawMaterialCategoryType.Made);
        viewModel.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Ingredient, "In-House"), "a Made category's folder is among the ingredient ones");

        // Esc and an empty name add nothing.
        await RunAsync(viewModel.List.TreeMenuFor(ingredients).Single(i => i.Header == "New Folder"));
        viewModel.List.CancelTreeName();
        await RunAsync(viewModel.List.TreeMenuFor(ingredients).Single(i => i.Header == "New Folder"));
        await viewModel.List.CommitTreeNameAsync(string.Empty);
        TitlesIn(ingredients).Should().Equal("Dairy", "Flour", "In-House");
        viewModel.Categories.Should().HaveCount(4);
    });

    [Test]
    public Task A_Made_categorys_folder_starts_made_ingredients_and_takes_only_them() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var inHouseId = await AddCategoryAsync("In-House", RawMaterialCategoryType.Made);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var syrupId = await AddIngredientAsync("Simple Syrup", inHouseId, RawMaterialSource.Made);
        var viewModel = await LoadedViewModelAsync();
        var inHouse = TreeFolder(viewModel, RawMaterialType.Ingredient, "In-House");

        TitlesIn(viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour", "In-House");
        inHouse.Materials.Select(l => l.Title).Should().Equal("Simple Syrup");
        Headers(viewModel.List.TreeMenuFor(inHouse)).Should().StartWith(["New Made Ingredient Here", "New Folder"]);
        viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Move to").Items
            .Select(i => i.Header).Should().Equal(["Flour"], "a purchased ingredient can't go in a Made category");
        viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(syrupId)).Single(i => i.Header == "Move to").Items
            .Select(i => i.Header).Should().Equal(["In-House"], "and a made one goes in nothing else");
        RawMaterialTree.MoveTo(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour"), viewModel.List.RawMaterialTree.Find(syrupId)!.RawMaterial)
            .Should().BeNull("a drop on an ingredient category does nothing for a made ingredient");
        RawMaterialTree.MoveTo(viewModel.List.RawMaterialTree.Find(syrupId), viewModel.List.RawMaterialTree.Find(breadFlourId)!.RawMaterial)
            .Should().BeNull("nor a drop on a made ingredient for a purchased one");

        await RunAsync(viewModel.List.TreeMenuFor(inHouse).Single(i => i.Header == "New Made Ingredient Here"));

        (viewModel.EditingType, viewModel.EditingSource).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Made));
        viewModel.EditingWeightLossPercent.Should().Be(3m, "a made ingredient starts at 3%, as New and a Source change start it");
        viewModel.EditingCategory!.Id.Should().Be(inHouseId);

        viewModel.CategoryOptions.Select(c => c.Name).Should().Equal("In-House");
        viewModel.History.IsDirty.Should().BeFalse("nothing is typed yet");

        // Made purchased instead, it's no longer one In-House can hold.
        Edit(viewModel, () => viewModel.EditingSource = RawMaterialSource.Purchased);

        viewModel.EditingCategory.Should().BeNull();
        viewModel.CategoryOptions.Select(c => c.Name).Should().Equal("Flour");
    });

    [Test]
    public Task Rename_Category_types_the_name_in_place_and_the_picker_follows() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        var flour = TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour");

        await RunAsync(viewModel.List.TreeMenuFor(flour).Single(i => i.Header == "Rename Category"));

        viewModel.List.TreeNameEdit.Should().Be(new TreeNameEdit(flour, "Flour"));
        await viewModel.List.CommitTreeNameAsync("Flours");

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.RawMaterialCategories.SingleAsync(Cancellation);
            (category.Name, category.Type).Should().Be(("Flours", RawMaterialCategoryType.Ingredient));
        }

        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flours").Materials.Should().ContainSingle();
        viewModel.EditingCategory!.Name.Should().Be("Flours");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Delete_Category_is_greyed_while_a_material_is_in_it_and_deletes_an_empty_one_once_asked() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        await AddIngredientAsync("Bread Flour", flourId);
        await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        // With a search on, the folder shows only one of them, but both keep the category.
        viewModel.List.SearchText = "Bread";
        var flourMenu = viewModel.List.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour"));
        Headers(flourMenu).Should().Equal("New Ingredient Here", "New Folder", "Rename Category", "Delete Category (holds 2)");
        flourMenu.Single(i => i.Header.StartsWith("Delete Category", StringComparison.Ordinal)).IsEnabled.Should().BeFalse();
        viewModel.List.SelectedTreeNode = TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour");
        viewModel.List.TreeShortcut("Del").Should().BeNull("Del does nothing where the menu's item is greyed");
        viewModel.List.SearchText = string.Empty;

        await RunAsync(viewModel.List.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Dairy")).Single(i => i.Header == "Delete Category"));

        viewModel.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Dairy' category? It holds no materials. This can't be undone.");
        viewModel.Categories.Select(c => c.Name).Should().Equal("Flour");
        TitlesIn(viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour");
    });

    [Test]
    public Task Delete_on_a_material_asks_first_and_the_form_starts_afresh_only_if_it_was_showing_it() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(cakeFlourId)).Single(i => i.Header == "Delete"));

        question.Should().StartWith("Delete 'Cake Flour'?");
        viewModel.RawMaterials.Select(m => m.Name).Should().Equal("Bread Flour");
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId, "the one showing wasn't the one deleted");

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Delete"));

        viewModel.RawMaterials.Should().BeEmpty();
        viewModel.SelectedRawMaterial.Should().BeNull();
        viewModel.EditingName.Should().BeEmpty();
    });

    [Test]
    public Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        var breadFlour = viewModel.List.RawMaterialTree.Find(breadFlourId)!;

        viewModel.List.SelectedTreeNode = breadFlour;
        viewModel.List.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == viewModel.List.RenameMaterialCommand && i.Parameter == breadFlour);
        viewModel.List.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == viewModel.List.DeleteMaterialCommand && i.Parameter == breadFlour);

        viewModel.List.SelectedTreeNode = TreeFolder(viewModel, RawMaterialType.Ingredient, "Dairy");
        viewModel.List.TreeShortcut("F2")!.Command.Should().BeSameAs(viewModel.List.RenameCategoryCommand);
        viewModel.List.TreeShortcut("Del")!.Command.Should().BeSameAs(viewModel.List.DeleteCategoryCommand);

        viewModel.List.SelectedTreeNode = viewModel.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);
        viewModel.List.TreeShortcut("F2").Should().BeNull("a type can't be renamed");

        viewModel.List.SelectedTreeNode = breadFlour;
        viewModel.List.SelectedTreeNodes.Add(viewModel.List.RawMaterialTree.Find(cakeFlourId)!);
        viewModel.List.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    });

    [Test]
    public Task Picking_a_row_picks_its_material_and_one_opened_from_elsewhere_is_picked_alone() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var ryeFlourId = await AddIngredientAsync("Rye Flour", flourId);
        var viewModel = await LoadedViewModelAsync();

        viewModel.List.SelectedTreeNode = viewModel.List.RawMaterialTree.Find(breadFlourId);
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);

        viewModel.List.SelectedTreeNode = TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour");
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId, "a folder picked leaves the material showing");

        viewModel.List.SelectedTreeNode = viewModel.List.RawMaterialTree.Find(breadFlourId);
        viewModel.List.SelectedTreeNodes.Add(viewModel.List.RawMaterialTree.Find(cakeFlourId)!);
        viewModel.OpenRecord(ryeFlourId);

        viewModel.List.SelectedTreeNodes.Should().Equal(viewModel.List.RawMaterialTree.Find(ryeFlourId)!);
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.RawMaterialTree.Find(ryeFlourId));
    });

    [Test]
    public Task Undoing_a_move_back_into_a_category_deleted_since_is_refused_and_says_why() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        await RunAsync(MoveToItem(viewModel, breadFlourId, "Dry Goods"));
        await new RawMaterialCategoryRepository(CreateDbContextFactory()).DeleteAsync(flourId, Cancellation);

        await viewModel.List.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("The category to move it to no longer exists.");
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
    });

    [Test]
    public Task The_folders_left_closed_stay_closed_next_time_and_a_new_category_opens() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var store = new InMemoryViewStateStore();
        (await LoadedViewModelAsync(store)).Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedViewModelAsync(store);

        TreeFolder(first, RawMaterialType.Ingredient, "Flour").IsExpanded = false;

        await Settle.EventuallyAsync(
            () => store.ReadList(RawMaterialsList.ClosedFoldersKey)?.Contains($"Ingredient/{flourId}") == true,
            () => string.Join(", ", store.ReadList(RawMaterialsList.ClosedFoldersKey) ?? []));
        first.Dispose();

        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        var next = await LoadedViewModelAsync(store);
        TreeFolder(next, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse();
        TreeFolder(next, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeTrue();
        TreeFolder(next, RawMaterialType.Ingredient, "Dairy").IsExpanded.Should().BeTrue("a category new since opens, as every folder first did");

        // One closed just before the app closes is written as it closes.
        TreeFolder(next, RawMaterialType.Packaging, "Boxes").IsExpanded = false;
        next.Dispose();
        store.ReadList(RawMaterialsList.ClosedFoldersKey).Should().Contain($"Packaging/{boxesId}");
    });

    [Test]
    public Task The_folders_the_list_kept_open_before_open_again_and_the_rest_start_closed() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var store = new InMemoryViewStateStore();
        store.WriteList(RawMaterialsList.OpenFoldersKey, ["Ingredient", $"Ingredient/{flourId}", "Packaging"]);

        var viewModel = await LoadedViewModelAsync(store);

        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue();
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse();

        // Kept as closed folders from then on.
        viewModel.Dispose();
        store.ReadList(RawMaterialsList.ClosedFoldersKey).Should().Contain($"Packaging/{boxesId}").And.NotContain($"Ingredient/{flourId}");
    });

    private static IEnumerable<string> Headers(IEnumerable<TreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    private static IEnumerable<string> TitlesIn(RawMaterialFolderNode folder) => folder.Children.Select(c => c.Title);

    private static RawMaterialFolderNode TreeFolder(RawMaterialsViewModel viewModel, RawMaterialType type, string title) =>
        viewModel.List.RawMaterialTree.TypeFolder(type).Children.OfType<RawMaterialFolderNode>().Single(f => f.Title == title);

    /// <summary>The material's menu's Move To item for <paramref name="category"/>.</summary>
    private static TreeMenuItem MoveToItem(RawMaterialsViewModel viewModel, int rawMaterialId, string category) =>
        viewModel.List.TreeMenuFor(viewModel.List.RawMaterialTree.Find(rawMaterialId)).Single(i => i.Header == "Move to").Items.Single(i => i.Header == category);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    [Test]
    public Task Picking_another_material_over_unsaved_changes_asks_and_Cancel_stays_on_them() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Strong Bread Flour";

        Click(viewModel, viewModel.List.RawMaterialTree.Find(cakeFlourId)!);
        await Settle.UntilAsync(viewModel, () => asked.Count == 1, () => $"asked={asked.Count}");

        asked.Single().Should().StartWith("Save the changes to Strong Bread Flour?");
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.RawMaterialTree.Find(breadFlourId), "the list goes back to the row showing");
        viewModel.EditingName.Should().Be("Strong Bread Flour");
        viewModel.History.IsDirty.Should().BeTrue();
    });

    [Test]
    [Arguments(UnsavedChangesAnswer.Save, "Strong Bread Flour")]
    [Arguments(UnsavedChangesAnswer.DontSave, "Bread Flour")]
    public Task Picking_another_material_over_unsaved_changes_opens_it_once_they_are_saved_or_not(UnsavedChangesAnswer answer, string savedName) => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        AnswerUnsavedChanges(viewModel, answer);
        viewModel.EditingName = "Strong Bread Flour";

        Click(viewModel, viewModel.List.RawMaterialTree.Find(cakeFlourId)!);
        await Settle.UntilAsync(viewModel,
            () => viewModel.SelectedRawMaterial?.Id == cakeFlourId && viewModel.AreDetailsReady && !viewModel.History.IsDirty,
            () => $"selected={viewModel.SelectedRawMaterial?.Id} ready={viewModel.AreDetailsReady}");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingName.Should().Be("Cake Flour");
        (await NameOfAsync(breadFlourId)).Should().Be(savedName);
    });

    [Test]
    public Task New_over_unsaved_changes_asks_and_Cancel_keeps_them() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Strong Bread Flour";

        await viewModel.NewCommand.Execute();

        asked.Should().ContainSingle();
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
        viewModel.EditingName.Should().Be("Strong Bread Flour");
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        viewModel.EditingName = "Strong Bread Flour";

        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();

        await Settle.UntilAsync(viewModel,
            () => viewModel.EditingName == "Bread Flour" && viewModel.AreDetailsReady && !viewModel.History.IsDirty,
            () => $"name={viewModel.EditingName}");
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
        (await NameOfAsync(breadFlourId)).Should().Be("Bread Flour");
    });

    // No puts a new material back blank. What's typed into it next has to count, or it's lost with no question asked.
    [Test]
    public Task What_is_typed_after_no_on_a_new_material_counts_as_unsaved() => UiThread.RunAsync(async () =>
    {
        var viewModel = await LoadedViewModelAsync();
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        viewModel.EditingName = "Bread Flour";
        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        viewModel.EditingName.Should().BeEmpty();
        viewModel.History.IsDirty.Should().BeFalse();

        viewModel.EditingName = "Strong Bread Flour";

        viewModel.History.IsDirty.Should().BeTrue();
        await viewModel.ResolveUnsavedChangesAsync(revert: false);
        asked.Should().HaveCount(2);
    });

    // A second click on the way somewhere while the question's still up: it stays, and isn't asked twice.
    [Test]
    public Task A_second_question_while_one_is_up_is_turned_away() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        var answer = new TaskCompletionSource<UnsavedChangesAnswer>();
        var asked = 0;
        viewModel.ConfirmUnsavedChanges.RegisterHandler(async context =>
        {
            asked++;
            context.SetOutput(await answer.Task);
        });
        viewModel.EditingName = "Strong Bread Flour";

        var first = viewModel.ResolveUnsavedChangesAsync(revert: false);
        var second = await viewModel.ResolveUnsavedChangesAsync(revert: false);

        second.Should().BeFalse();
        asked.Should().Be(1);

        answer.SetResult(UnsavedChangesAnswer.DontSave);
        (await first).Should().BeTrue("the first is still answered");
    });

    /// <summary>Answers the question about unsaved changes with <paramref name="answer"/>, collecting each question asked.</summary>
    private static List<string> AnswerUnsavedChanges(RawMaterialsViewModel viewModel, UnsavedChangesAnswer answer)
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
    private static void Click(RawMaterialsViewModel viewModel, object row)
    {
        viewModel.List.SelectedTreeNodes.Clear();
        viewModel.List.SelectedTreeNodes.Add(row);
    }

    private async Task<string> NameOfAsync(int rawMaterialId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.Where(m => m.Id == rawMaterialId).Select(m => m.Name).SingleAsync(Cancellation);
    }

    private static async Task RunAsync(TreeMenuItem item)
    {
        switch (item.Command)
        {
            case ReactiveCommand<IReadOnlyList<RawMaterialMove>, RxVoid> move:
                await move.Execute((IReadOnlyList<RawMaterialMove>)item.Parameter!);
                break;
            case ReactiveCommand<RawMaterialLeafNode, RxVoid> onMaterial:
                await onMaterial.Execute((RawMaterialLeafNode)item.Parameter!);
                break;
            case ReactiveCommand<RawMaterialFolderNode, RxVoid> onFolder:
                await onFolder.Execute((RawMaterialFolderNode?)item.Parameter!);
                break;
            case ReactiveCommand<RawMaterialCategoryType, RxVoid> onCategoryType:
                await onCategoryType.Execute((RawMaterialCategoryType)item.Parameter!);
                break;
            default:
                throw new InvalidOperationException($"'{item.Header}' runs nothing a test knows how to wait for.");
        }
    }

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(RawMaterialsViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    private async Task<int> AddCategoryAsync(string name, RawMaterialCategoryType type)
    {
        await using var dbContext = CreateDbContext();
        var category = new RawMaterialCategory { Name = name, Type = type };
        dbContext.RawMaterialCategories.Add(category);
        await dbContext.SaveChangesAsync(Cancellation);
        return category.Id;
    }

    private async Task<int> AddIngredientAsync(string name, int categoryId, RawMaterialSource source = RawMaterialSource.Purchased)
    {
        int? recipeId = source == RawMaterialSource.Made ? await AddEmptyRecipeAsync() : null;
        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Name = name,
            Type = RawMaterialType.Ingredient,
            CategoryId = categoryId,
            Source = source,
            WeightLossPercent = source == RawMaterialSource.Made ? 0m : null,
            SubAssemblyId = recipeId,
        };

        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    private async Task<int> AddPackagingAsync(string name, int categoryId)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        await using var dbContext = CreateDbContext();
        var material = new RawMaterial
        {
            Name = name,
            Type = RawMaterialType.Packaging,
            CategoryId = categoryId,
            Source = RawMaterialSource.Purchased,
            BaseUnitId = pieceId,
        };
        dbContext.RawMaterials.Add(material);
        await dbContext.SaveChangesAsync(Cancellation);
        return material.Id;
    }

    private async Task<int> CategoryOfAsync(int rawMaterialId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.RawMaterials.Where(m => m.Id == rawMaterialId).Select(m => m.CategoryId).SingleAsync(Cancellation);
    }

    private async Task<RawMaterialsViewModel> LoadedViewModelAsync(IViewStateStore? viewStateStore = null)
    {
        var viewModel = CreateViewModel(viewStateStore);
        await viewModel.LoadCommand.Execute();
        return viewModel;
    }

    /// <summary>The screen with <paramref name="rawMaterialId"/> open on the form, its details in.</summary>
    private async Task<RawMaterialsViewModel> OpenedViewModelAsync(int rawMaterialId)
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.OpenRecord(rawMaterialId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.AreDetailsReady && viewModel.SelectedRawMaterial?.Id == rawMaterialId,
            () => $"ready={viewModel.AreDetailsReady}");
        return viewModel;
    }

    private RawMaterialsViewModel CreateViewModel(IViewStateStore? viewStateStore)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

        return new RawMaterialsViewModel(
            new RawMaterialRepository(factory),
            new RawMaterialPriceHistoryRepository(factory),
            new RawMaterialBrandPriceRepository(factory),
            new IngredientBomLineRepository(factory),
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new RawMaterialCategoryValidator(),
            new RawMaterialValidator(),
            new RawMaterialUnitValidator(),
            new RawMaterialBrandPriceValidator(),
            new UnitOfMeasureRepository(factory),
            viewStateStore ?? new InMemoryViewStateStore(),
            new CostingDefaultsRepository(factory),
            new LoggerConfiguration().CreateLogger());
    }
}
