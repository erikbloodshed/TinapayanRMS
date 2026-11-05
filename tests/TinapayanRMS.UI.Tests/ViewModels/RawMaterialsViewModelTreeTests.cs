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
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The Raw Materials list's right-click menus, its keys and its selection, as ProductsViewModelTreeTests has them for the
/// Products list: what each menu offers a material, several materials, a folder and the list itself, and what each item
/// does, run as the menu runs it. A material only moves between categories of its own type.
/// </summary>
[Collection(DatabaseCollection.B)]
public class RawMaterialsViewModelTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public Task A_materials_menu_moves_it_to_another_category_of_its_type_and_offers_rename_and_delete() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        var leaf = viewModel.RawMaterialTree.Find(breadFlourId)!;
        viewModel.SelectedTreeNode = leaf;

        var menu = viewModel.TreeMenuFor(leaf);

        Headers(menu).Should().Equal("Move to", "Rename…", "Delete");
        menu.Single(i => i.Header == "Rename…").Gesture.Should().Be("F2");
        menu.Single(i => i.Header == "Delete").Gesture.Should().Be("Del");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        moveTo.Select(i => i.Header).Should().Equal(["Dry Goods", "Flour"], "only its own type's categories");
        moveTo.Single(i => i.Header == "Flour").IsEnabled.Should().BeFalse("it's in that folder already");
    });

    [Fact]
    public Task Move_to_saves_at_once_says_so_and_Undo_on_the_notice_puts_it_back() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(MoveToItem(viewModel, breadFlourId, "Dry Goods"));

        viewModel.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Dry Goods").Materials.Should().ContainSingle();
        viewModel.MoveNotice.Should().Be("Moved Bread Flour to Ingredients › Dry Goods");

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(flourId);
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").Materials.Should().ContainSingle();
        viewModel.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        (await viewModel.UndoMoveCommand.CanExecute.FirstAsync()).Should().BeFalse();
    });

    [Fact]
    public Task Several_materials_picked_move_together_and_the_one_showing_stays_on_the_form() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var sugarId = await AddIngredientAsync("Sugar", dryGoodsId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        var tree = viewModel.RawMaterialTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        viewModel.SelectedTreeNodes.Add(tree.Find(cakeFlourId)!);
        viewModel.SelectedTreeNodes.Add(tree.Find(sugarId)!);
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);

        var menu = viewModel.TreeMenuFor(tree.Find(cakeFlourId));

        Headers(menu).Should().Equal(["Move 3 Materials to"], "renaming or deleting is for one at a time");
        await RunAsync(menu[0].Items.Single(i => i.Header == "Dry Goods"));

        viewModel.ErrorMessage.Should().BeNull();
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
        (await CategoryOfAsync(cakeFlourId)).Should().Be(dryGoodsId);
        viewModel.MoveNotice.Should().Be("Moved 2 materials to Ingredients › Dry Goods", "Sugar was there already");
        viewModel.SelectedTreeMaterials.Select(l => l.RawMaterial.Id).Should().Equal(breadFlourId, cakeFlourId, sugarId);
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
    });

    [Fact]
    public Task Ingredients_and_packaging_picked_together_have_nowhere_to_move() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeBoxId = await AddPackagingAsync("Cake Box", boxesId);
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedTreeNode = viewModel.RawMaterialTree.Find(breadFlourId);
        viewModel.SelectedTreeNodes.Add(viewModel.RawMaterialTree.Find(cakeBoxId)!);

        viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(cakeBoxId)).Should().BeEmpty();
    });

    [Fact]
    public Task A_move_of_the_material_showing_moves_its_Category_picker_and_leaves_what_else_is_typed_to_save() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        Edit(viewModel, () => viewModel.EditingCostBufferPercent = 5m);

        await RunAsync(MoveToItem(viewModel, breadFlourId, "Dry Goods"));

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingCategory!.Id.Should().Be(dryGoodsId);
        viewModel.EditingCostBufferPercent.Should().Be(5m);
        viewModel.History.IsDirty.Should().BeTrue("the buffer is still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingCostBufferPercent.Should().Be(0m);
        viewModel.EditingCategory!.Id.Should().Be(dryGoodsId, "no undo takes back a move that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Fact]
    public Task Rename_on_the_material_showing_saves_the_name_and_leaves_what_else_is_typed_to_save() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        TextPromptRequest? asked = null;
        viewModel.PromptForName.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput("Strong Flour");
        });
        Edit(viewModel, () => viewModel.EditingCostBufferPercent = 5m);

        await RunAsync(viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Rename…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked.Should().Be(new TextPromptRequest("Rename Raw Material", "Name", "Bread Flour"));
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.RawMaterials.SingleAsync(m => m.Id == breadFlourId, Cancellation);
            (saved.Name, saved.CostBufferPercent).Should().Be(("Strong Flour", 0m), "the rename saves the name alone");
        }

        viewModel.EditingName.Should().Be("Strong Flour");
        viewModel.RawMaterialTree.Find(breadFlourId)!.Title.Should().Be("Strong Flour");
        viewModel.History.IsDirty.Should().BeTrue("the buffer is still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingName.Should().Be("Strong Flour", "no undo takes back a rename that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Fact]
    public Task A_rename_to_a_name_in_use_is_refused_and_says_why() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput("Cake Flour"));

        await RunAsync(viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Rename…"));

        viewModel.ErrorMessage.Should().Be("That name is already in use.");
        viewModel.RawMaterialTree.Find(breadFlourId)!.Title.Should().Be("Bread Flour");
    });

    [Fact]
    public Task New_Here_starts_a_material_of_the_folders_type_in_its_category_that_Save_files_there() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes")).Single(i => i.Header == "New Packaging Material Here"));

        viewModel.SelectedRawMaterial.Should().BeNull();
        viewModel.EditingType.Should().Be(RawMaterialType.Packaging);
        viewModel.EditingCategory!.Id.Should().Be(boxesId);
        viewModel.History.IsDirty.Should().BeFalse("nothing is typed yet");

        await RunAsync(viewModel.TreeMenuFor(viewModel.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Single(i => i.Header == "New Ingredient Here"));

        viewModel.EditingType.Should().Be(RawMaterialType.Ingredient);
        viewModel.EditingCategory.Should().BeNull("a type's folder has no category");

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour")).Single(i => i.Header == "New Ingredient Here"));
        Edit(viewModel, () => viewModel.EditingName = "Rye Flour");
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").Materials.Select(l => l.Title).Should().Equal("Rye Flour");
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.RawMaterialTree.Find(viewModel.SelectedRawMaterial!.Id));
        (await CategoryOfAsync(viewModel.SelectedRawMaterial.Id)).Should().Be(flourId);
    });

    [Fact]
    public Task The_type_and_list_menus_add_materials_and_categories_and_open_or_close_every_folder() => StaThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var viewModel = await LoadedViewModelAsync();
        var ingredients = viewModel.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);

        Headers(viewModel.TreeMenuFor(ingredients)).Should().Equal("New Ingredient Here", "New Category…", "New Made Category…", "Expand All", "Collapse All");
        Headers(viewModel.TreeMenuFor(viewModel.RawMaterialTree.TypeFolder(RawMaterialType.Packaging)))
            .Should().Equal("New Packaging Material Here", "New Category…", "Expand All", "Collapse All");
        var listMenu = viewModel.TreeMenuFor(null);
        Headers(listMenu).Should().Equal(
            "New Raw Material", "New Ingredient Category…", "New Made Category…", "New Packaging Category…", "Expand All", "Collapse All");

        await RunAsync(listMenu.Single(i => i.Header == "Collapse All"));
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse();
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse();
        viewModel.RawMaterialTree.Roots.Should().OnlyContain(r => r.IsExpanded);

        await RunAsync(viewModel.TreeMenuFor(ingredients).Single(i => i.Header == "Expand All"));
        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue();
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse("only that type's folders");

        await RunAsync(listMenu.Single(i => i.Header == "Expand All"));
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeTrue();
    });

    [Fact]
    public Task New_Category_adds_a_folder_in_its_type_and_picks_it() => StaThread.RunAsync(async () =>
    {
        await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var viewModel = await LoadedViewModelAsync();
        var answer = "Dairy";
        TextPromptRequest? asked = null;
        viewModel.PromptForName.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(answer);
        });

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour")).Single(i => i.Header == "New Category…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked!.Message.Should().Be("A folder in Ingredients.");
        viewModel.Categories.Should().ContainSingle(c => c.Name == "Dairy" && c.Type == RawMaterialCategoryType.Ingredient);
        viewModel.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Ingredient, "Dairy"));

        answer = "Labels";
        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "New Packaging Category…"));

        viewModel.Categories.Should().ContainSingle(c => c.Name == "Labels" && c.Type == RawMaterialCategoryType.Packaging);
        viewModel.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Packaging, "Labels"));

        answer = "flour";
        await RunAsync(viewModel.TreeMenuFor(viewModel.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Single(i => i.Header == "New Category…"));

        viewModel.Categories.Should().HaveCount(3, "a name that's one of the type's categories already adds none");
        viewModel.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour"));

        answer = "In-House";
        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "New Made Category…"));

        asked.Message.Should().Be("A folder in Ingredients, for made ingredients only.");
        viewModel.Categories.Should().ContainSingle(c => c.Name == "In-House" && c.Type == RawMaterialCategoryType.Made);
        viewModel.SelectedTreeNode.Should().BeSameAs(TreeFolder(viewModel, RawMaterialType.Ingredient, "In-House"), "a Made category's folder is among the ingredient ones");
    });

    [Fact]
    public Task A_Made_categorys_folder_starts_made_ingredients_and_takes_only_them() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var inHouseId = await AddCategoryAsync("In-House", RawMaterialCategoryType.Made);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var syrupId = await AddIngredientAsync("Simple Syrup", inHouseId, RawMaterialSource.Made);
        var viewModel = await LoadedViewModelAsync();
        var inHouse = TreeFolder(viewModel, RawMaterialType.Ingredient, "In-House");

        TitlesIn(viewModel.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour", "In-House");
        inHouse.Materials.Select(l => l.Title).Should().Equal("Simple Syrup");
        Headers(viewModel.TreeMenuFor(inHouse)).Should().StartWith(["New Made Ingredient Here", "New Category…"]);
        viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Move to").Items
            .Select(i => i.Header).Should().Equal(["Flour"], "a purchased ingredient can't go in a Made category");
        viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(syrupId)).Single(i => i.Header == "Move to").Items
            .Select(i => i.Header).Should().Equal(["In-House"], "and a made one goes in nothing else");
        RawMaterialTree.MoveTo(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour"), viewModel.RawMaterialTree.Find(syrupId)!.RawMaterial)
            .Should().BeNull("a drop on an ingredient category does nothing for a made ingredient");
        RawMaterialTree.MoveTo(viewModel.RawMaterialTree.Find(syrupId), viewModel.RawMaterialTree.Find(breadFlourId)!.RawMaterial)
            .Should().BeNull("nor a drop on a made ingredient for a purchased one");

        await RunAsync(viewModel.TreeMenuFor(inHouse).Single(i => i.Header == "New Made Ingredient Here"));

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

    [Fact]
    public Task Rename_Category_renames_its_folder_and_the_picker_follows() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput("Flours"));

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour")).Single(i => i.Header == "Rename Category…"));

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

    [Fact]
    public Task Delete_Category_is_greyed_while_a_material_is_in_it_and_deletes_an_empty_one_once_asked() => StaThread.RunAsync(async () =>
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
        viewModel.SearchText = "Bread";
        var flourMenu = viewModel.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour"));
        Headers(flourMenu).Should().Equal("New Ingredient Here", "New Category…", "Rename Category…", "Delete Category (holds 2)");
        flourMenu.Single(i => i.Header.StartsWith("Delete Category", StringComparison.Ordinal)).IsEnabled.Should().BeFalse();
        viewModel.SelectedTreeNode = TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour");
        viewModel.TreeShortcut("Del").Should().BeNull("Del does nothing where the menu's item is greyed");
        viewModel.SearchText = string.Empty;

        await RunAsync(viewModel.TreeMenuFor(TreeFolder(viewModel, RawMaterialType.Ingredient, "Dairy")).Single(i => i.Header == "Delete Category"));

        viewModel.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Dairy' category? It holds no materials. This can't be undone.");
        viewModel.Categories.Select(c => c.Name).Should().Equal("Flour");
        TitlesIn(viewModel.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour");
    });

    [Fact]
    public Task Delete_on_a_material_asks_first_and_the_form_starts_afresh_only_if_it_was_showing_it() => StaThread.RunAsync(async () =>
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

        await RunAsync(viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(cakeFlourId)).Single(i => i.Header == "Delete"));

        question.Should().StartWith("Delete 'Cake Flour'?");
        viewModel.RawMaterials.Select(m => m.Name).Should().Equal("Bread Flour");
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId, "the one showing wasn't the one deleted");

        await RunAsync(viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(breadFlourId)).Single(i => i.Header == "Delete"));

        viewModel.RawMaterials.Should().BeEmpty();
        viewModel.SelectedRawMaterial.Should().BeNull();
        viewModel.EditingName.Should().BeEmpty();
    });

    [Fact]
    public Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        var breadFlour = viewModel.RawMaterialTree.Find(breadFlourId)!;

        viewModel.SelectedTreeNode = breadFlour;
        viewModel.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == viewModel.RenameMaterialCommand && i.Parameter == breadFlour);
        viewModel.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == viewModel.DeleteMaterialCommand && i.Parameter == breadFlour);

        viewModel.SelectedTreeNode = TreeFolder(viewModel, RawMaterialType.Ingredient, "Dairy");
        viewModel.TreeShortcut("F2")!.Command.Should().BeSameAs(viewModel.RenameCategoryCommand);
        viewModel.TreeShortcut("Del")!.Command.Should().BeSameAs(viewModel.DeleteCategoryCommand);

        viewModel.SelectedTreeNode = viewModel.RawMaterialTree.TypeFolder(RawMaterialType.Ingredient);
        viewModel.TreeShortcut("F2").Should().BeNull("a type can't be renamed");

        viewModel.SelectedTreeNode = breadFlour;
        viewModel.SelectedTreeNodes.Add(viewModel.RawMaterialTree.Find(cakeFlourId)!);
        viewModel.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    });

    [Fact]
    public Task Picking_a_row_picks_its_material_and_one_opened_from_elsewhere_is_picked_alone() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var ryeFlourId = await AddIngredientAsync("Rye Flour", flourId);
        var viewModel = await LoadedViewModelAsync();

        viewModel.SelectedTreeNode = viewModel.RawMaterialTree.Find(breadFlourId);
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);

        viewModel.SelectedTreeNode = TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour");
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId, "a folder picked leaves the material showing");

        viewModel.SelectedTreeNode = viewModel.RawMaterialTree.Find(breadFlourId);
        viewModel.SelectedTreeNodes.Add(viewModel.RawMaterialTree.Find(cakeFlourId)!);
        viewModel.OpenRawMaterial(ryeFlourId);

        viewModel.SelectedTreeNodes.Should().Equal(viewModel.RawMaterialTree.Find(ryeFlourId)!);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.RawMaterialTree.Find(ryeFlourId));
    });

    [Fact]
    public Task Undoing_a_move_back_into_a_category_deleted_since_is_refused_and_says_why() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var dryGoodsId = await AddCategoryAsync("Dry Goods", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var viewModel = await LoadedViewModelAsync();
        await RunAsync(MoveToItem(viewModel, breadFlourId, "Dry Goods"));
        await new RawMaterialCategoryRepository(CreateDbContextFactory()).DeleteAsync(flourId, Cancellation);

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().Be("The category to move it to no longer exists.");
        (await CategoryOfAsync(breadFlourId)).Should().Be(dryGoodsId);
    });

    [Fact]
    public Task The_folders_left_closed_stay_closed_next_time_and_a_new_category_opens() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var store = new InMemoryViewStateStore();
        (await LoadedViewModelAsync(store)).Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedViewModelAsync(store);

        TreeFolder(first, RawMaterialType.Ingredient, "Flour").IsExpanded = false;

        await Settle.EventuallyAsync(
            () => store.ReadList(RawMaterialsViewModel.ClosedFoldersKey)?.Contains($"Ingredient/{flourId}") == true,
            () => string.Join(", ", store.ReadList(RawMaterialsViewModel.ClosedFoldersKey) ?? []));
        first.Dispose();

        await AddCategoryAsync("Dairy", RawMaterialCategoryType.Ingredient);
        var next = await LoadedViewModelAsync(store);
        TreeFolder(next, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse();
        TreeFolder(next, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeTrue();
        TreeFolder(next, RawMaterialType.Ingredient, "Dairy").IsExpanded.Should().BeTrue("a category new since opens, as every folder first did");

        // One closed just before the app closes is written as it closes.
        TreeFolder(next, RawMaterialType.Packaging, "Boxes").IsExpanded = false;
        next.Dispose();
        store.ReadList(RawMaterialsViewModel.ClosedFoldersKey).Should().Contain($"Packaging/{boxesId}");
    });

    [Fact]
    public Task The_folders_the_list_kept_open_before_open_again_and_the_rest_start_closed() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var boxesId = await AddCategoryAsync("Boxes", RawMaterialCategoryType.Packaging);
        var store = new InMemoryViewStateStore();
        store.WriteList(RawMaterialsViewModel.OpenFoldersKey, ["Ingredient", $"Ingredient/{flourId}", "Packaging"]);

        var viewModel = await LoadedViewModelAsync(store);

        TreeFolder(viewModel, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue();
        TreeFolder(viewModel, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeFalse();

        // Kept as closed folders from then on.
        viewModel.Dispose();
        store.ReadList(RawMaterialsViewModel.ClosedFoldersKey).Should().Contain($"Packaging/{boxesId}").And.NotContain($"Ingredient/{flourId}");
    });

    private static IEnumerable<string> Headers(IEnumerable<TreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    private static IEnumerable<string> TitlesIn(RawMaterialFolderNode folder) => folder.Children.Select(c => c.Title);

    private static RawMaterialFolderNode TreeFolder(RawMaterialsViewModel viewModel, RawMaterialType type, string title) =>
        viewModel.RawMaterialTree.TypeFolder(type).Children.OfType<RawMaterialFolderNode>().Single(f => f.Title == title);

    /// <summary>The material's menu's Move To item for <paramref name="category"/>.</summary>
    private static TreeMenuItem MoveToItem(RawMaterialsViewModel viewModel, int rawMaterialId, string category) =>
        viewModel.TreeMenuFor(viewModel.RawMaterialTree.Find(rawMaterialId)).Single(i => i.Header == "Move to").Items.Single(i => i.Header == category);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    [Fact]
    public Task Picking_another_material_over_unsaved_changes_asks_and_Cancel_stays_on_them() => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Strong Bread Flour";

        Click(viewModel, viewModel.RawMaterialTree.Find(cakeFlourId)!);
        await Settle.UntilAsync(viewModel, () => asked.Count == 1, () => $"asked={asked.Count}");

        asked.Single().Should().StartWith("Save the changes to Strong Bread Flour?");
        viewModel.SelectedRawMaterial!.Id.Should().Be(breadFlourId);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.RawMaterialTree.Find(breadFlourId), "the list goes back to the row showing");
        viewModel.EditingName.Should().Be("Strong Bread Flour");
        viewModel.History.IsDirty.Should().BeTrue();
    });

    [Theory]
    [InlineData(UnsavedChangesAnswer.Save, "Strong Bread Flour")]
    [InlineData(UnsavedChangesAnswer.DontSave, "Bread Flour")]
    public Task Picking_another_material_over_unsaved_changes_opens_it_once_they_are_saved_or_not(UnsavedChangesAnswer answer, string savedName) => StaThread.RunAsync(async () =>
    {
        var flourId = await AddCategoryAsync("Flour", RawMaterialCategoryType.Ingredient);
        var breadFlourId = await AddIngredientAsync("Bread Flour", flourId);
        var cakeFlourId = await AddIngredientAsync("Cake Flour", flourId);
        var viewModel = await OpenedViewModelAsync(breadFlourId);
        AnswerUnsavedChanges(viewModel, answer);
        viewModel.EditingName = "Strong Bread Flour";

        Click(viewModel, viewModel.RawMaterialTree.Find(cakeFlourId)!);
        await Settle.UntilAsync(viewModel,
            () => viewModel.SelectedRawMaterial?.Id == cakeFlourId && viewModel.AreDetailsReady && !viewModel.History.IsDirty,
            () => $"selected={viewModel.SelectedRawMaterial?.Id} ready={viewModel.AreDetailsReady}");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingName.Should().Be("Cake Flour");
        (await NameOfAsync(breadFlourId)).Should().Be(savedName);
    });

    [Fact]
    public Task New_over_unsaved_changes_asks_and_Cancel_keeps_them() => StaThread.RunAsync(async () =>
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

    [Fact]
    public Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved() => StaThread.RunAsync(async () =>
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
    [Fact]
    public Task What_is_typed_after_no_on_a_new_material_counts_as_unsaved() => StaThread.RunAsync(async () =>
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
    [Fact]
    public Task A_second_question_while_one_is_up_is_turned_away() => StaThread.RunAsync(async () =>
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
        viewModel.SelectedTreeNodes.Clear();
        viewModel.SelectedTreeNodes.Add(row);
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

    /// <summary>One finished change, as the Controls/ behaviours bracket a gesture in the app.</summary>
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
        viewModel.OpenRawMaterial(rawMaterialId);
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
