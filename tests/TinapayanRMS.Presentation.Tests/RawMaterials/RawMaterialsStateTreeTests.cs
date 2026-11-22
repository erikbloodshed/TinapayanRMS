using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.RawMaterials;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Tests.RawMaterials;

/// <summary>
/// The Raw Materials list's right-click menus, its keys and its selection, as ProductsStateTreeTests has them for the
/// Products list: what each menu offers a material, several materials, a folder and the list itself, and what each item
/// does, run as the menu runs it. A material only moves between categories of its own type.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class RawMaterialsStateTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_materials_menu_moves_it_to_another_category_of_its_type_and_offers_rename_and_delete() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await LoadedStateAsync();
        var leaf = state.View.List.RawMaterialTree.Find(breadFlourId)!;
        state.View.List.SelectedTreeNode = leaf;

        var menu = state.View.List.TreeMenuFor(leaf);

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
        var state = await LoadedStateAsync();
        IListTreeScreen screen = state.View.List;
        var breadFlour = state.View.List.RawMaterialTree.Find(breadFlourId)!;

        screen.CanDrag(breadFlour).Should().BeTrue();
        screen.CanDrag(TreeFolder(state, RawMaterialType.Ingredient, "Flour")).Should().BeFalse("a folder isn't dragged");
        screen.CanDrop([breadFlour], TreeFolder(state, RawMaterialType.Packaging, "Boxes")).Should().BeFalse("a material's type is fixed");
        screen.CanDrop([breadFlour], state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().BeFalse("a type's folder has no category to give");

        (await screen.DropAsync([breadFlour], TreeFolder(state, RawMaterialType.Ingredient, "Dry Goods"))).Should().BeSameAs(breadFlour);

        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        state.View.List.MoveNotice.Should().Be("Moved Bread Flour to Ingredients › Dry Goods");
    });

    [Test]
    public Task A_drop_made_while_a_move_is_still_saving_does_nothing() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var caneSugarId = await AddIngredientAsync("Cane Sugar", flourId);
        var state = await LoadedStateAsync();
        IListTreeScreen screen = state.View.List;

        var firstMove = state.View.List.MoveMaterialsCommand.ExecuteAsync([new RawMaterialMove(breadFlourId, dryGoodsId)]);
        state.View.List.MoveMaterialsCommand.CanExecute([]).Should().BeFalse("the move is under way, as a drop would find it");
        (await screen.DropAsync([state.View.List.RawMaterialTree.Find(caneSugarId)!], TreeFolder(state, RawMaterialType.Ingredient, "Dry Goods")))
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
        var state = await LoadedStateAsync();

        await RunAsync(MoveToItem(state, breadFlourId, "Dry Goods"));

        state.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        TreeFolder(state, RawMaterialType.Ingredient, "Dry Goods").Materials.Should().ContainSingle();
        state.View.List.MoveNotice.Should().Be("Moved Bread Flour to Ingredients › Dry Goods");

        await state.View.List.UndoMoveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(flourId);
        TreeFolder(state, RawMaterialType.Ingredient, "Flour").Materials.Should().ContainSingle();
        state.View.List.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        state.View.List.UndoMoveCommand.CanExecute().Should().BeFalse();
    });

    [Test]
    public Task Several_materials_picked_move_together_and_the_one_showing_stays_on_the_form() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var sugarId = await AddIngredientAsync("Sugar", dryGoodsId);
        var state = await OpenedStateAsync(breadFlourId);
        var tree = state.View.List.RawMaterialTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        state.View.List.SelectedTreeNodes.Add(tree.Find(cakeFlourId)!);
        state.View.List.SelectedTreeNodes.Add(tree.Find(sugarId)!);
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId);

        var menu = state.View.List.TreeMenuFor(tree.Find(cakeFlourId));

        Headers(menu).Should().Equal(["Move 3 Materials to"], "renaming or deleting is for one at a time");
        await RunAsync(menu[0].Items.Single(i => i.Header == "Dry Goods"));

        state.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        (await CategoryOfAsync(cakeFlourId)).Should().Be(dryGoodsId);
        state.View.List.MoveNotice.Should().Be("Moved 2 materials to Ingredients › Dry Goods", "Sugar was there already");
        state.View.List.SelectedTreeMaterials.Select(l => l.RawMaterial.Id).Should().Equal(breadFlourId, cakeFlourId, sugarId);
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
    });

    [Test]
    public Task Ingredients_and_packaging_picked_together_have_nowhere_to_move() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeBoxId = await AddPackagingAsync("Cake Box", boxesId);
        var state = await LoadedStateAsync();
        state.View.List.SelectedTreeNode = state.View.List.RawMaterialTree.Find(breadFlourId);
        state.View.List.SelectedTreeNodes.Add(state.View.List.RawMaterialTree.Find(cakeBoxId)!);

        state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(cakeBoxId)).Should().BeEmpty();
    });

    [Test]
    public Task A_move_of_the_material_showing_moves_its_Category_picker_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        Edit(state, () => state.PurchaseCost.EditingCostBufferPercent = 5m);

        await RunAsync(MoveToItem(state, breadFlourId, "Dry Goods"));

        state.ErrorMessage.Should().BeNull();
        state.EditingCategory!.Id.Should().Be(dryGoodsId);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(5m);
        state.History.IsDirty.Should().BeTrue("the buffer is still to be saved");

        state.TryUndo().Should().BeTrue();

        state.PurchaseCost.EditingCostBufferPercent.Should().Be(0m);
        state.EditingCategory!.Id.Should().Be(dryGoodsId, "no undo takes back a move that's saved");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Rename_types_the_name_in_place_saves_it_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        Edit(state, () => state.PurchaseCost.EditingCostBufferPercent = 5m);
        var breadFlour = state.View.List.RawMaterialTree.Find(breadFlourId)!;

        await RunAsync(state.View.List.TreeMenuFor(breadFlour).Single(i => i.Header == "Rename"));

        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(breadFlour, "Bread Flour"));

        await state.View.List.CommitTreeNameAsync(" Strong Flour ");
        await state.View.List.CommitTreeNameAsync("Strong Flour Again");

        state.ErrorMessage.Should().BeNull();
        state.View.List.TreeNameEdit.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.RawMaterials.SingleAsync(m => m.Id == breadFlourId, Cancellation);
            (saved.Name, saved.CostBufferPercent).Should().Be(("Strong Flour", 0m), "the rename saves the name alone, once");
        }

        state.EditingName.Should().Be("Strong Flour");
        state.View.List.RawMaterialTree.Find(breadFlourId)!.Title.Should().Be("Strong Flour");
        state.History.IsDirty.Should().BeTrue("the buffer is still to be saved");

        state.TryUndo().Should().BeTrue();

        state.EditingName.Should().Be("Strong Flour", "no undo takes back a rename that's saved");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_rename_to_a_name_in_use_is_refused_says_why_and_brings_the_box_back() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        await AddIngredientAsync("Cake Flour", flourId);
        var state = await LoadedStateAsync();
        var breadFlour = state.View.List.RawMaterialTree.Find(breadFlourId)!;

        await RunAsync(state.View.List.TreeMenuFor(breadFlour).Single(i => i.Header == "Rename"));
        await state.View.List.CommitTreeNameAsync("Cake Flour");

        state.ErrorMessage.Should().Be("That name is already in use.");
        state.View.List.RawMaterialTree.Find(breadFlourId)!.Title.Should().Be("Bread Flour");
        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(breadFlour, "Cake Flour"));

        // Esc, an empty name and the same name leave it be; so does a search.
        state.View.List.CancelTreeName();
        state.View.List.TreeNameEdit.Should().BeNull();
        state.View.List.SelectedTreeNode = breadFlour;
        await RunAsync(state.View.List.TreeShortcut("F2")!);
        await state.View.List.CommitTreeNameAsync("  ");
        await RunAsync(state.View.List.TreeShortcut("F2")!);
        await state.View.List.CommitTreeNameAsync("Bread Flour");
        await RunAsync(state.View.List.TreeShortcut("F2")!);
        state.View.List.SearchText = "flour";
        state.View.List.TreeNameEdit.Should().BeNull();
        (await NameOfAsync(breadFlourId)).Should().Be("Bread Flour");
    });

    [Test]
    public Task New_names_the_material_in_its_folder_and_the_row_stays_until_Save_files_it() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var state = await LoadedStateAsync();
        var flour = TreeFolder(state, RawMaterialType.Ingredient, "Flour");
        state.View.List.SearchText = "nothing like it";

        await RunAsync(state.View.List.TreeMenuFor(flour).Single(i => i.Header == "New Ingredient Here"));

        state.View.List.SearchText.Should().BeEmpty("the search is cleared, so the new row shows");
        var row = state.View.List.RawMaterialTree.PendingMaterial!;
        flour.Children.Should().Equal(row);
        flour.Count.Should().Be(0, "it isn't a material until it's saved");
        state.View.List.SelectedTreeNode.Should().BeSameAs(row);
        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(row, "New ingredient"));
        state.View.List.TreeMenuFor(row).Should().BeEmpty();
        state.View.List.SelectedTreeMaterials.Should().BeEmpty();
        state.EditingCategory!.Id.Should().Be(flourId);

        await state.View.List.CommitTreeNameAsync("Rye Flour");

        state.ErrorMessage.Should().BeNull();
        state.RawMaterials.Should().BeEmpty("a material needs more than a name, so Save files it");
        state.View.List.TreeNameEdit.Should().BeNull();
        row.Title.Should().Be("Rye Flour");
        state.EditingName.Should().Be("Rye Flour");
        state.History.IsDirty.Should().BeTrue("Save is offered for it");

        await state.LoadCommand.ExecuteAsync();
        flour.Children.Should().Equal([row], "a load keeps it");
        state.View.List.SelectedTreeNode.Should().BeSameAs(row);

        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.View.List.RawMaterialTree.PendingMaterial.Should().BeNull();
        var saved = state.View.List.RawMaterialTree.Find(state.SelectedRawMaterial!.Id)!;
        flour.Children.Should().Equal(saved);
        saved.Title.Should().Be("Rye Flour");
        state.View.List.SelectedTreeNode.Should().BeSameAs(saved);
    });

    [Test]
    public Task New_from_the_header_names_an_ingredient_in_the_Ingredients_folder_and_Esc_or_Dont_Save_takes_the_row_away() => UiThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var state = await LoadedStateAsync();
        AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        var ingredients = state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);

        await state.NewCommand.ExecuteAsync();

        var row = state.View.List.RawMaterialTree.PendingMaterial!;
        TitlesIn(ingredients).Should().Equal(["Flour", "New ingredient"], "with no category yet, it's last in its type's folder");
        state.View.List.CancelTreeName();
        TitlesIn(ingredients).Should().Equal("Flour");
        state.View.List.SelectedTreeNode.Should().BeNull();
        state.SelectedRawMaterial.Should().BeNull("the form stays on the blank new material");

        await state.NewCommand.ExecuteAsync();
        await state.View.List.CommitTreeNameAsync("Rye Flour");
        TitlesIn(ingredients).Should().Equal("Flour", "Rye Flour");

        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("Don't Save puts the form back blank");

        state.View.List.RawMaterialTree.PendingMaterial.Should().BeNull("put back blank, it has no row");
        TitlesIn(ingredients).Should().Equal("Flour");
        state.RawMaterials.Should().BeEmpty();
    });

    [Test]
    public Task New_Here_starts_a_material_of_the_folders_type_in_its_category_that_Save_files_there() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var state = await LoadedStateAsync();

        await RunAsync(state.View.List.TreeMenuFor(TreeFolder(state, RawMaterialType.Packaging, "Boxes")).Single(i => i.Header == "New Packaging Material Here"));

        state.SelectedRawMaterial.Should().BeNull();
        state.EditingType.Should().Be(RawMaterialType.Packaging);
        state.EditingCategory!.Id.Should().Be(boxesId);
        state.History.IsDirty.Should().BeFalse("nothing is typed yet");

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Single(i => i.Header == "New Ingredient Here"));

        state.EditingType.Should().Be(RawMaterialType.Ingredient);
        state.EditingCategory.Should().BeNull("a type's folder has no category");

        await RunAsync(state.View.List.TreeMenuFor(TreeFolder(state, RawMaterialType.Ingredient, "Flour")).Single(i => i.Header == "New Ingredient Here"));
        Edit(state, () => state.EditingName = "Rye Flour");
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        TreeFolder(state, RawMaterialType.Ingredient, "Flour").Materials.Select(l => l.Title).Should().Equal("Rye Flour");
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.RawMaterialTree.Find(state.SelectedRawMaterial!.Id));
        (await CategoryOfAsync(state.SelectedRawMaterial.Id)).Should().Be(flourId);
    });

    [Test]
    public Task Copy_starts_a_new_material_with_only_the_type_category_and_base_unit_that_Save_adds() => UiThread.RunAsync(async () =>
    {
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var spoonId = await AddPackagingAsync("Disposable Spoon", boxesId);
        var state = await OpenedStateAsync(spoonId);
        Edit(state, () => state.PurchaseCost.EditingCostBufferPercent = 25m);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(spoonId)).Single(i => i.Header == "Copy"));
        var copy = state.View.Form!;

        asked.Should().BeEmpty("the spoon's unsaved buffer stays in its own tab");
        copy.Should().NotBeSameAs(state);
        state.PurchaseCost.EditingCostBufferPercent.Should().Be(25m);
        copy.SelectedRawMaterial.Should().BeNull("the copy is a new material");
        copy.EditingName.Should().Be("Disposable Spoon Copy");
        copy.EditingType.Should().Be(RawMaterialType.Packaging);
        copy.EditingCategory!.Id.Should().Be(boxesId);
        copy.EditingBaseUnit!.Name.Should().Be("piece");
        copy.PurchaseCost.EditingCostBufferPercent.Should().Be(10m, "the buffer is the new material's default, not the spoon's");
        copy.PurchaseCost.BrandPriceRows.Should().BeEmpty();
        copy.CanChangeTypeAndSource.Should().BeTrue();
        copy.History.IsDirty.Should().BeTrue("the copy is there to be saved");

        await copy.SaveCommand.ExecuteAsync();

        copy.ErrorMessage.Should().BeNull();
        TreeFolder(copy, RawMaterialType.Packaging, "Boxes").Materials.Select(l => l.Title).Should().Equal("Disposable Spoon", "Disposable Spoon Copy");
        await using var dbContext = CreateDbContext();
        var saved = await dbContext.RawMaterials.SingleAsync(m => m.Id == copy.SelectedRawMaterial!.Id, Cancellation);
        (saved.Type, saved.CategoryId, saved.BaseUnitId).Should().Be((RawMaterialType.Packaging, boxesId, state.View.List.RawMaterialTree.Find(spoonId)!.RawMaterial.BaseUnitId));
    });

    [Test]
    public Task Copy_names_past_a_copy_already_there_and_keeps_a_made_ingredient_made() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var inHouseId = await AddCategoryAsync("In-House", RawMaterialCategoryType.Made);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        await AddIngredientAsync("bread flour copy", flourId);
        var syrupId = await AddIngredientAsync("Simple Syrup", inHouseId, RawMaterialSource.Made);
        var state = await LoadedStateAsync();

        // The blank form the view opens on holds nothing unsaved, so the copy takes its place.
        await RunAsync(state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Copy"));

        state.View.Form.Should().BeSameAs(state);
        state.EditingName.Should().Be("Bread Flour Copy 2", "names are told apart without their case");
        (state.EditingType, state.EditingSource).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Purchased));
        state.EditingCategory!.Id.Should().Be(flourId);
        state.EditingBaseUnit.Should().BeNull("an ingredient's base unit is always the gram");

        // That copy is there to be saved, so the next one opens in a tab of its own.
        await RunAsync(state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(syrupId)).Single(i => i.Header == "Copy"));
        var syrupCopy = state.View.Form!;

        syrupCopy.Should().NotBeSameAs(state);
        state.EditingName.Should().Be("Bread Flour Copy 2");
        syrupCopy.EditingName.Should().Be("Simple Syrup Copy");
        (syrupCopy.EditingType, syrupCopy.EditingSource).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Made));
        syrupCopy.EditingCategory!.Id.Should().Be(inHouseId);
        syrupCopy.Recipe.SubAssembly.Should().BeNull("its recipe isn't copied");
    });

    [Test]
    public Task The_type_and_list_menus_add_materials_and_categories_and_open_or_close_every_folder() => UiThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var state = await LoadedStateAsync();
        var ingredients = state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);

        Headers(state.View.List.TreeMenuFor(ingredients)).Should().Equal("New Ingredient Here", "New Folder", "New Made Folder", "Expand All", "Collapse All");
        Headers(state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Packaging)))
            .Should().Equal("New Packaging Material Here", "New Folder", "Expand All", "Collapse All");
        var listMenu = state.View.List.TreeMenuFor(null);
        Headers(listMenu).Should().Equal(
            "New Raw Material", "New Ingredient Folder", "New Made Folder", "New Packaging Folder", "Expand All", "Collapse All");

        await RunAsync(listMenu.Single(i => i.Header == "Collapse All"));
        TreeFolder(state, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse();
        TreeFolder(state, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse();
        state.View.List.RawMaterialTree.Roots.Should().OnlyContain(r => r.IsExpanded);

        await RunAsync(state.View.List.TreeMenuFor(ingredients).Single(i => i.Header == "Expand All"));
        TreeFolder(state, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue();
        TreeFolder(state, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse("only that type's folders");

        await RunAsync(listMenu.Single(i => i.Header == "Expand All"));
        TreeFolder(state, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeTrue();
    });

    [Test]
    public Task New_Folder_names_a_category_in_place_in_its_type_and_picks_it() => UiThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var state = await LoadedStateAsync();
        var ingredients = state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);

        await RunAsync(state.View.List.TreeMenuFor(TreeFolder(state, RawMaterialType.Ingredient, "Flour")).Single(i => i.Header == "New Folder"));

        var folder = state.View.List.RawMaterialTree.PendingFolder!;
        TitlesIn(ingredients).Should().Equal("Flour", "New folder");
        state.View.List.SelectedTreeNode.Should().BeSameAs(folder);
        state.View.List.TreeNameEdit!.Node.Should().BeSameAs(folder);
        state.View.List.TreeNameEdit.Hint.Should().Be("A folder in Ingredients.");
        state.View.List.TreeMenuFor(folder).Should().BeEmpty();

        await state.View.List.CommitTreeNameAsync("Dairy");

        state.ErrorMessage.Should().BeNull();
        state.View.List.RawMaterialTree.PendingFolder.Should().BeNull();
        state.Categories.Should().ContainSingle(c => c.Name == "Dairy" && c.Type == RawMaterialCategoryType.Ingredient);
        state.View.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(state, RawMaterialType.Ingredient, "Dairy"));

        await RunAsync(state.View.List.TreeMenuFor(null).Single(i => i.Header == "New Packaging Folder"));
        await state.View.List.CommitTreeNameAsync("Labels");

        state.Categories.Should().ContainSingle(c => c.Name == "Labels" && c.Type == RawMaterialCategoryType.Packaging);
        state.View.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(state, RawMaterialType.Packaging, "Labels"));

        await RunAsync(state.View.List.TreeMenuFor(ingredients).Single(i => i.Header == "New Folder"));
        await state.View.List.CommitTreeNameAsync("flour");

        state.Categories.Should().HaveCount(3, "a name that's one of the type's categories already adds none");
        state.View.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(state, RawMaterialType.Ingredient, "Flour"));

        await RunAsync(state.View.List.TreeMenuFor(null).Single(i => i.Header == "New Made Folder"));
        state.View.List.TreeNameEdit!.Hint.Should().Be("A folder in Ingredients, for made ingredients only.");
        await state.View.List.CommitTreeNameAsync("In-House");

        state.Categories.Should().ContainSingle(c => c.Name == "In-House" && c.Type == RawMaterialCategoryType.Made);
        state.View.List.SelectedTreeNode.Should().BeSameAs(TreeFolder(state, RawMaterialType.Ingredient, "In-House"), "a Made category's folder is among the ingredient ones");

        // Esc and an empty name add nothing.
        await RunAsync(state.View.List.TreeMenuFor(ingredients).Single(i => i.Header == "New Folder"));
        state.View.List.CancelTreeName();
        await RunAsync(state.View.List.TreeMenuFor(ingredients).Single(i => i.Header == "New Folder"));
        await state.View.List.CommitTreeNameAsync(string.Empty);
        TitlesIn(ingredients).Should().Equal("Dairy", "Flour", "In-House");
        state.Categories.Should().HaveCount(4);
    });

    [Test]
    public Task A_Made_categorys_folder_starts_made_ingredients_and_takes_only_them() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var inHouseId = await AddCategoryAsync("In-House", RawMaterialCategoryType.Made);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var syrupId = await AddIngredientAsync("Simple Syrup", inHouseId, RawMaterialSource.Made);
        var state = await LoadedStateAsync();
        var inHouse = TreeFolder(state, RawMaterialType.Ingredient, "In-House");

        TitlesIn(state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour", "In-House");
        inHouse.Materials.Select(l => l.Title).Should().Equal("Simple Syrup");
        Headers(state.View.List.TreeMenuFor(inHouse)).Should().StartWith(["New Made Ingredient Here", "New Folder"]);
        state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Move to").Items
            .Select(i => i.Header).Should().Equal(["Flour"], "a purchased ingredient can't go in a Made category");
        state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(syrupId)).Single(i => i.Header == "Move to").Items
            .Select(i => i.Header).Should().Equal(["In-House"], "and a made one goes in nothing else");
        RawMaterialTree.MoveTo(TreeFolder(state, RawMaterialType.Ingredient, "Flour"), state.View.List.RawMaterialTree.Find(syrupId)!.RawMaterial)
            .Should().BeNull("a drop on an ingredient category does nothing for a made ingredient");
        RawMaterialTree.MoveTo(state.View.List.RawMaterialTree.Find(syrupId), state.View.List.RawMaterialTree.Find(breadFlourId)!.RawMaterial)
            .Should().BeNull("nor a drop on a made ingredient for a purchased one");

        await RunAsync(state.View.List.TreeMenuFor(inHouse).Single(i => i.Header == "New Made Ingredient Here"));

        (state.EditingType, state.EditingSource).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Made));
        state.EditingWeightLossPercent.Should().Be(3m, "a made ingredient starts at 3%, as New and a Source change start it");
        state.EditingCategory!.Id.Should().Be(inHouseId);

        state.CategoryOptions.Select(c => c.Name).Should().Equal("In-House");
        state.History.IsDirty.Should().BeFalse("nothing is typed yet");

        // Made purchased instead, it's no longer one In-House can hold.
        Edit(state, () => state.ChangeSource(RawMaterialSource.Purchased));

        state.EditingCategory.Should().BeNull();
        state.CategoryOptions.Select(c => c.Name).Should().Equal("Flour");
    });

    [Test]
    public Task The_toolbars_New_starts_a_material_where_the_row_picked_in_the_list_is() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var inHouseId = await AddCategoryAsync("In-House", RawMaterialCategoryType.Made);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var syrupId = await AddIngredientAsync("Simple Syrup", inHouseId, RawMaterialSource.Made);
        var state = await OpenedStateAsync(breadFlourId);
        var list = state.View.List;

        async Task<(RawMaterialType Type, RawMaterialSource Source, int? CategoryId)> NewFromToolbarAsync()
        {
            await list.NewMaterialInCommand.ExecuteAsync(null);
            list.CancelTreeName();
            var form = state.View.Form!;
            return (form.EditingType, form.EditingSource, form.EditingCategory?.Id);
        }

        // The material showing is the row picked: the new one goes in its category.
        list.SelectedTreeNode.Should().BeSameAs(list.RawMaterialTree.Find(breadFlourId));
        (await NewFromToolbarAsync()).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Purchased, (int?)flourId));

        // A made ingredient picked, or its Made folder: a made one, in that category.
        list.SelectedTreeNode = list.RawMaterialTree.Find(syrupId);
        (await NewFromToolbarAsync()).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Made, (int?)inHouseId));
        list.SelectedTreeNode = list.RawMaterialTree.CategoryFolder(inHouseId);
        (await NewFromToolbarAsync()).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Made, (int?)inHouseId));

        // A category's folder picked: in it. A type's folder: that type, with no category yet.
        list.SelectedTreeNode = list.RawMaterialTree.CategoryFolder(boxesId);
        (await NewFromToolbarAsync()).Should().Be((RawMaterialType.Packaging, RawMaterialSource.Purchased, (int?)boxesId));
        list.SelectedTreeNode = list.RawMaterialTree.TypeFolder(RawMaterialType.Packaging);
        (await NewFromToolbarAsync()).Should().Be((RawMaterialType.Packaging, RawMaterialSource.Purchased, (int?)null));

        // Nothing picked: a new ingredient, as New starts one.
        list.SelectedTreeNode = null;
        (await NewFromToolbarAsync()).Should().Be((RawMaterialType.Ingredient, RawMaterialSource.Purchased, (int?)null));
    });

    [Test]
    public Task Rename_Category_types_the_name_in_place_and_the_picker_follows() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        var flour = TreeFolder(state, RawMaterialType.Ingredient, "Flour");

        await RunAsync(state.View.List.TreeMenuFor(flour).Single(i => i.Header == "Rename Category"));

        state.View.List.TreeNameEdit.Should().Be(new TreeNameEdit(flour, "Flour"));
        await state.View.List.CommitTreeNameAsync("Flours");

        state.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var category = await dbContext.RawMaterialCategories.SingleAsync(Cancellation);
            (category.Name, category.Type).Should().Be(("Flours", RawMaterialCategoryType.Ingredient));
        }

        TreeFolder(state, RawMaterialType.Ingredient, "Flours").Materials.Should().ContainSingle();
        state.EditingCategory!.Name.Should().Be("Flours");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Delete_Category_is_greyed_while_a_material_is_in_it_and_deletes_an_empty_one_once_asked() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        await AddIngredientAsync("Bread Flour", flourId);
        await AddIngredientAsync("Cake Flour", flourId);
        var state = await LoadedStateAsync();
        string? question = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        // With a search on, the folder shows only one of them, but both keep the category.
        state.View.List.SearchText = "Bread";
        var flourMenu = state.View.List.TreeMenuFor(TreeFolder(state, RawMaterialType.Ingredient, "Flour"));
        Headers(flourMenu).Should().Equal("New Ingredient Here", "New Folder", "Rename Category", "Delete Category (holds 2)");
        flourMenu.Single(i => i.Header.StartsWith("Delete Category", StringComparison.Ordinal)).IsEnabled.Should().BeFalse();
        state.View.List.SelectedTreeNode = TreeFolder(state, RawMaterialType.Ingredient, "Flour");
        state.View.List.TreeShortcut("Del").Should().BeNull("Del does nothing where the menu's item is greyed");
        state.View.List.SearchText = string.Empty;

        await RunAsync(state.View.List.TreeMenuFor(TreeFolder(state, RawMaterialType.Ingredient, "Dairy")).Single(i => i.Header == "Delete Category"));

        state.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Dairy' category? It holds no materials. This can't be undone.");
        state.Categories.Select(c => c.Name).Should().Equal("Flour");
        TitlesIn(state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour");
    });

    [Test]
    public Task Delete_on_a_material_asks_first_and_closes_its_tab_only_if_one_holds_it() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        string? question = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(cakeFlourId)).Single(i => i.Header == "Delete"));

        question.Should().StartWith("Delete 'Cake Flour'?");
        state.RawMaterials.Select(m => m.Name).Should().Equal("Bread Flour");
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId, "the one showing wasn't the one deleted");
        state.View.Tabs.All.Should().ContainSingle("no tab held the one deleted");

        await RunAsync(state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Delete"));

        state.View.RawMaterials.Should().BeEmpty();
        state.View.Tabs.All.Should().BeEmpty("the tab holding the one deleted closed, as it's gone");
        state.View.Form.Should().BeNull();
    });

    [Test]
    public Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var state = await LoadedStateAsync();
        var breadFlour = state.View.List.RawMaterialTree.Find(breadFlourId)!;

        state.View.List.SelectedTreeNode = breadFlour;
        state.View.List.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == state.View.List.RenameMaterialCommand && i.Parameter == breadFlour);
        state.View.List.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == state.View.List.DeleteMaterialCommand && i.Parameter == breadFlour);

        state.View.List.SelectedTreeNode = TreeFolder(state, RawMaterialType.Ingredient, "Dairy");
        state.View.List.TreeShortcut("F2")!.Command.Should().BeSameAs(state.View.List.RenameCategoryCommand);
        state.View.List.TreeShortcut("Del")!.Command.Should().BeSameAs(state.View.List.DeleteCategoryCommand);

        state.View.List.SelectedTreeNode = state.View.List.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);
        state.View.List.TreeShortcut("F2").Should().BeNull("a type can't be renamed");

        state.View.List.SelectedTreeNode = breadFlour;
        state.View.List.SelectedTreeNodes.Add(state.View.List.RawMaterialTree.Find(cakeFlourId)!);
        state.View.List.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    });

    [Test]
    public Task Picking_a_row_picks_its_material_and_one_opened_from_elsewhere_is_picked_alone() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var ryeFlourId = await AddIngredientAsync("Rye Flour", flourId);
        var state = await LoadedStateAsync();

        state.View.List.SelectedTreeNode = state.View.List.RawMaterialTree.Find(breadFlourId);
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId);

        state.View.List.SelectedTreeNode = TreeFolder(state, RawMaterialType.Ingredient, "Flour");
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId, "a folder picked leaves the material showing");

        state.View.List.SelectedTreeNode = state.View.List.RawMaterialTree.Find(breadFlourId);
        state.View.List.SelectedTreeNodes.Add(state.View.List.RawMaterialTree.Find(cakeFlourId)!);
        state.OpenRecord(ryeFlourId);

        state.View.List.SelectedTreeNodes.Should().Equal(state.View.List.RawMaterialTree.Find(ryeFlourId)!);
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.RawMaterialTree.Find(ryeFlourId));
    });

    [Test]
    public Task Undoing_a_move_back_into_a_category_deleted_since_is_refused_and_says_why() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await LoadedStateAsync();
        await RunAsync(MoveToItem(state, breadFlourId, "Dry Goods"));
        await new RawMaterialCategoryRepository(CreateDbContextFactory()).DeleteAsync(flourId, Cancellation);

        await state.View.List.UndoMoveCommand.ExecuteAsync();

        state.ErrorMessage.Should().Be("The category to move it to no longer exists.");
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
    });

    [Test]
    public Task The_folders_left_closed_stay_closed_next_time_and_a_new_category_opens() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var store = new InMemoryViewStateStore();
        (await LoadedStateAsync(store)).View.Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedStateAsync(store);

        TreeFolder(first, RawMaterialType.Ingredient, "Flour").IsExpanded = false;

        await Settle.EventuallyAsync(
            () => store.ReadList(RawMaterialsList.ClosedFoldersKey)?.Contains($"Ingredient/{flourId}") == true,
            () => string.Join(", ", store.ReadList(RawMaterialsList.ClosedFoldersKey) ?? []));
        first.View.Dispose();

        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        var next = await LoadedStateAsync(store);
        TreeFolder(next, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse();
        TreeFolder(next, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeTrue();
        TreeFolder(next, RawMaterialType.Ingredient, "Dairy").IsExpanded.Should().BeTrue("a category new since opens, as every folder first did");

        // One closed just before the app closes is written as it closes.
        TreeFolder(next, RawMaterialType.Packaging, "Boxes").IsExpanded = false;
        next.View.Dispose();
        store.ReadList(RawMaterialsList.ClosedFoldersKey).Should().Contain($"Packaging/{boxesId}");
    });

    [Test]
    public Task The_folders_the_list_kept_open_before_open_again_and_the_rest_start_closed() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var store = new InMemoryViewStateStore();
        store.WriteList(RawMaterialsList.OpenFoldersKey, ["Ingredient", $"Ingredient/{flourId}", "Packaging"]);

        var state = await LoadedStateAsync(store);

        TreeFolder(state, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue();
        TreeFolder(state, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse();

        // Kept as closed folders from then on.
        state.View.Dispose();
        store.ReadList(RawMaterialsList.ClosedFoldersKey).Should().Contain($"Packaging/{boxesId}").And.NotContain($"Ingredient/{flourId}");
    });

    private static IEnumerable<string> Headers(IEnumerable<TreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    private static IEnumerable<string> TitlesIn(RawMaterialFolderNode folder) => folder.Children.Select(c => c.Title);

    private static RawMaterialFolderNode TreeFolder(RawMaterialForm state, RawMaterialType type, string title) =>
        state.View.List.RawMaterialTree.TypeFolder(type).Children.OfType<RawMaterialFolderNode>().Single(f => f.Title == title);

    /// <summary>The material's menu's Move To item for <paramref name="category"/>.</summary>
    private static TreeMenuItem MoveToItem(RawMaterialForm state, int rawMaterialId, string category) =>
        state.View.List.TreeMenuFor(state.View.List.RawMaterialTree.Find(rawMaterialId)).Single(i => i.Header == "Move to").Items.Single(i => i.Header == category);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    [Test]
    public Task Picking_another_material_over_unsaved_changes_opens_it_in_a_tab_of_its_own_and_asks_nothing() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        state.EditingName = "Strong Bread Flour";

        Click(state, state.View.List.RawMaterialTree.Find(cakeFlourId)!);
        var cake = state.View.Form!;
        await Settle.UntilAsync(cake, () => cake.SelectedRawMaterial?.Id == cakeFlourId && cake.AreDetailsReady, () => $"selected={cake.SelectedRawMaterial?.Id}");

        asked.Should().BeEmpty("the changes stay in their own tab: nothing is left");
        cake.Should().NotBeSameAs(state);
        cake.EditingName.Should().Be("Cake Flour");
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
        state.EditingName.Should().Be("Strong Bread Flour");
        state.History.IsDirty.Should().BeTrue();
        state.View.Tabs.All.Select(tab => tab.Form).Should().Equal(state, cake);
        state.View.List.SelectedTreeNode.Should().BeSameAs(state.View.List.RawMaterialTree.Find(cakeFlourId), "the list follows the tab showing");
    });

    [Test]
    public Task A_pick_takes_the_preview_tabs_place_while_it_holds_nothing_unsaved_and_a_tab_kept_or_edited_stays() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var ryeFlourId = await AddIngredientAsync("Rye Flour", flourId);
        var state = await LoadedStateAsync();
        var view = state.View;

        // The blank form the view opens on is the preview tab: the first pick takes its place, and the next one's.
        Click(state, view.List.RawMaterialTree.Find(breadFlourId)!);
        Click(state, view.List.RawMaterialTree.Find(cakeFlourId)!);
        await Settle.UntilAsync(state, () => state.SelectedRawMaterial?.Id == cakeFlourId && state.AreDetailsReady);
        view.Tabs.All.Should().ContainSingle().Which.IsPreview.Should().BeTrue();

        // Kept by a double-click on its row: the next pick opens a tab of its own.
        view.List.KeepOpen(view.List.RawMaterialTree.Find(cakeFlourId)!);
        view.Tabs.Active!.IsPreview.Should().BeFalse();
        Click(state, view.List.RawMaterialTree.Find(ryeFlourId)!);
        var rye = view.Form!;
        await Settle.UntilAsync(rye, () => rye.SelectedRawMaterial?.Id == ryeFlourId && rye.AreDetailsReady);
        view.Tabs.All.Select(tab => tab.Form).Should().Equal(state, rye);
        view.Tabs.Preview!.Form.Should().BeSameAs(rye);

        // An edit keeps the preview tab too: the next pick doesn't take its place.
        rye.EditingName = "Dark Rye Flour";
        Click(state, view.List.RawMaterialTree.Find(breadFlourId)!);
        var bread = view.Form!;
        await Settle.UntilAsync(bread, () => bread.SelectedRawMaterial?.Id == breadFlourId && bread.AreDetailsReady);
        view.Tabs.All.Select(tab => tab.Form).Should().Equal(state, rye, bread);
        rye.EditingName.Should().Be("Dark Rye Flour");

        // A material open already shows in its own tab.
        Click(state, view.List.RawMaterialTree.Find(cakeFlourId)!);
        view.Form.Should().BeSameAs(state);
        view.Tabs.All.Should().HaveCount(3);
    });

    [Test]
    [Arguments(UnsavedChangesAnswer.Save, "Strong Bread Flour", true)]
    [Arguments(UnsavedChangesAnswer.DontSave, "Bread Flour", true)]
    [Arguments(UnsavedChangesAnswer.Cancel, "Bread Flour", false)]
    public Task Closing_a_tab_with_changes_asks_about_them_first(UnsavedChangesAnswer answer, string savedName, bool closes) => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        var view = state.View;
        var asked = AnswerUnsavedChanges(state, answer);
        state.EditingName = "Strong Bread Flour";
        Click(state, view.List.RawMaterialTree.Find(cakeFlourId)!);
        var breadTab = view.Tabs.Holding(breadFlourId)!;

        (await view.CloseTabAsync(breadTab)).Should().Be(closes);

        asked.Should().ContainSingle().Which.Should().StartWith("Save the changes to Strong Bread Flour?");
        view.Tabs.All.Contains(breadTab).Should().Be(!closes);
        (await NameOfAsync(breadFlourId)).Should().Be(savedName);
        if (!closes)
        {
            view.Tabs.Active.Should().BeSameAs(breadTab, "it's shown, so the question is about the form in view");
            state.EditingName.Should().Be("Strong Bread Flour");
        }
    });

    [Test]
    public Task Closing_a_tab_with_nothing_unsaved_asks_nothing_and_shows_the_one_before() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        var view = state.View;
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        view.Keep(view.Tabs.Active!);
        Click(state, view.List.RawMaterialTree.Find(cakeFlourId)!);

        (await view.CloseTabAsync(view.Tabs.Active!)).Should().BeTrue();

        asked.Should().BeEmpty();
        view.Form.Should().BeSameAs(state);
        view.List.SelectedTreeNode.Should().BeSameAs(view.List.RawMaterialTree.Find(breadFlourId), "the list follows the tab showing");
    });

    [Test]
    public Task New_over_unsaved_changes_opens_a_new_tab_and_keeps_them() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        state.EditingName = "Strong Bread Flour";

        await state.NewCommand.ExecuteAsync();

        asked.Should().BeEmpty();
        state.View.Form.Should().NotBeSameAs(state);
        state.View.Form!.EditingId.Should().Be(0);
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
        state.EditingName.Should().Be("Strong Bread Flour");
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        state.EditingName = "Strong Bread Flour";

        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();

        await Settle.UntilAsync(state,
            () => state.EditingName == "Bread Flour" && state.AreDetailsReady && !state.History.IsDirty,
            () => $"name={state.EditingName}");
        state.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
        (await NameOfAsync(breadFlourId)).Should().Be("Bread Flour");
    });

    // No puts a new material back blank. What's typed into it next has to count, or it's lost with no question asked.
    [Test]
    public Task What_is_typed_after_no_on_a_new_material_counts_as_unsaved() => UiThread.RunAsync(async () =>
    {
        var state = await LoadedStateAsync();
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        state.EditingName = "Bread Flour";
        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        state.EditingName.Should().BeEmpty();
        state.History.IsDirty.Should().BeFalse();

        state.EditingName = "Strong Bread Flour";

        state.History.IsDirty.Should().BeTrue();
        await state.ResolveUnsavedChangesAsync(revert: false);
        asked.Should().HaveCount(2);
    });

    // A second click on the way somewhere while the question's still up: it stays, and isn't asked twice.
    [Test]
    public Task A_second_question_while_one_is_up_is_turned_away() => UiThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var state = await OpenedStateAsync(breadFlourId);
        var answer = new TaskCompletionSource<UnsavedChangesAnswer>();
        var asked = 0;
        state.ConfirmUnsavedChanges.RegisterHandler(async context =>
        {
            asked++;
            context.SetOutput(await answer.Task);
        });
        state.EditingName = "Strong Bread Flour";

        var first = state.ResolveUnsavedChangesAsync(revert: false);
        var second = await state.ResolveUnsavedChangesAsync(revert: false);

        second.Should().BeFalse();
        asked.Should().Be(1);

        answer.SetResult(UnsavedChangesAnswer.DontSave);
        (await first).Should().BeTrue("the first is still answered");
    });

    /// <summary>Answers the question about unsaved changes with <paramref name="answer"/>, collecting each question asked.</summary>
    private static List<string> AnswerUnsavedChanges(RawMaterialForm state, UnsavedChangesAnswer answer)
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
    private static void Click(RawMaterialForm state, object row)
    {
        state.View.List.SelectedTreeNodes.Clear();
        state.View.List.SelectedTreeNodes.Add(row);
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
            case ScreenCommand<IReadOnlyList<RawMaterialMove>> move:
                await move.ExecuteAsync((IReadOnlyList<RawMaterialMove>)item.Parameter!);
                break;
            case ScreenCommand<RawMaterialLeafNode> onMaterial:
                await onMaterial.ExecuteAsync((RawMaterialLeafNode)item.Parameter!);
                break;
            case ScreenCommand<RawMaterialFolderNode?> onFolder:
                await onFolder.ExecuteAsync((RawMaterialFolderNode?)item.Parameter);
                break;
            case ScreenCommand<RawMaterialCategoryType> onCategoryType:
                await onCategoryType.ExecuteAsync((RawMaterialCategoryType)item.Parameter!);
                break;
            default:
                throw new InvalidOperationException($"'{item.Header}' runs nothing a test knows how to wait for.");
        }
    }

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(RawMaterialForm state, Action change)
    {
        var recorder = (IUndoRecorder)state;
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

    private async Task<RawMaterialForm> LoadedStateAsync(IViewStateStore? viewStateStore = null)
    {
        var state = CreateState(viewStateStore);
        await state.LoadCommand.ExecuteAsync();
        return state;
    }

    /// <summary>The screen with <paramref name="rawMaterialId"/> open on the form, its details in.</summary>
    private async Task<RawMaterialForm> OpenedStateAsync(int rawMaterialId)
    {
        var state = await LoadedStateAsync();
        state.OpenRecord(rawMaterialId);
        await Settle.UntilAsync(state,
            () => state.AreDetailsReady && state.SelectedRawMaterial?.Id == rawMaterialId,
            () => $"ready={state.AreDetailsReady}");
        return state;
    }

    private RawMaterialForm CreateState(IViewStateStore? viewStateStore)
    {
        var factory = CreateDbContextFactory();

        return RawMaterialsViews.Create(factory, viewStateStore).Form!;
    }
}
