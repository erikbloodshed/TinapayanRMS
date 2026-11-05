using System.Diagnostics;
using System.Globalization;
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
/// The Sub-Assemblies list's right-click menus, its keys and its selection, as RawMaterialsViewModelTreeTests has them for
/// the Raw Materials list: what each menu offers a sub-assembly, several sub-assemblies, a role's folder and the list
/// itself, and what each item does, run as the menu runs it. A recipe only moves between recipe roles, and a packaging set
/// stays in the Packaging role.
/// </summary>
[Collection(DatabaseCollection.B)]
public class SubAssembliesViewModelTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Fact]
    public async Task A_sub_assemblys_menu_moves_it_to_another_recipe_role_and_offers_rename_and_delete()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var doughId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        var leaf = viewModel.SubAssemblyTree.Find(doughId)!;
        viewModel.SelectedTreeNode = leaf;

        var menu = viewModel.TreeMenuFor(leaf);

        Headers(menu).Should().Equal("Move to", "Rename…", "Make In-House Ingredient…", "Delete");
        menu.Single(i => i.Header == "Rename…").Gesture.Should().Be("F2");
        menu.Single(i => i.Header == "Delete").Gesture.Should().Be("Del");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        moveTo.Select(i => i.Header).Should().Equal(["Dough", "Filling"], "only the recipe roles, in their order");
        moveTo.Single(i => i.Header == "Dough").IsEnabled.Should().BeFalse("it's in that folder already");
    }

    [Fact]
    public async Task A_packaging_set_has_nowhere_to_move_and_nor_has_one_picked_with_a_recipe()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var doughId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var setId = await AddPackagingSetAsync("Gift Set", packagingRoleId);
        var viewModel = await LoadedViewModelAsync();
        var set = viewModel.SubAssemblyTree.Find(setId)!;

        Headers(viewModel.TreeMenuFor(set)).Should().Equal("Rename…", "Delete");

        viewModel.SelectedTreeNode = viewModel.SubAssemblyTree.Find(doughId);
        viewModel.SelectedTreeNodes.Add(set);

        viewModel.TreeMenuFor(set).Should().BeEmpty();
    }

    [Fact]
    public async Task Move_to_saves_at_once_says_so_and_Undo_on_the_notice_puts_it_back()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(MoveToItem(viewModel, halayaId, "Filling"));

        viewModel.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        viewModel.SubAssemblyTree.Folder(fillingRoleId)!.SubAssemblies.Should().ContainSingle();
        viewModel.MoveNotice.Should().Be("Moved Ube Halaya to Filling");

        await viewModel.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(doughRoleId);
        viewModel.SubAssemblyTree.Folder(doughRoleId)!.SubAssemblies.Should().ContainSingle();
        viewModel.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        (await viewModel.UndoMoveCommand.CanExecute.FirstAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Several_picked_move_together_and_the_one_showing_stays_on_the_form()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var jamId = await AddRecipeAsync("Ube Jam", doughRoleId);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var viewModel = await OpenedViewModelAsync(halayaId);
        var tree = viewModel.SubAssemblyTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        viewModel.SelectedTreeNodes.Add(tree.Find(jamId)!);
        viewModel.SelectedTreeNodes.Add(tree.Find(custardId)!);
        viewModel.SelectedRow!.Id.Should().Be(halayaId);

        var menu = viewModel.TreeMenuFor(tree.Find(jamId));

        Headers(menu).Should().Equal(["Move 3 Sub-Assemblies to"], "renaming or deleting is for one at a time");
        await RunAsync(menu[0].Items.Single(i => i.Header == "Filling"));

        viewModel.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        (await RoleOfAsync(jamId)).Should().Be(fillingRoleId);
        viewModel.MoveNotice.Should().Be("Moved 2 sub-assemblies to Filling", "Custard was there already");
        viewModel.SelectedTreeSubAssemblies.Select(l => l.Row.Id).Should().Equal(halayaId, jamId, custardId);
        viewModel.SelectedRow!.Id.Should().Be(halayaId);
    }

    [Fact]
    public async Task A_move_of_the_one_showing_moves_its_Role_picker_and_leaves_what_else_is_typed_to_save()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var viewModel = await OpenedViewModelAsync(halayaId);
        Edit(viewModel, () => viewModel.Lines[0].Grams = 250m);

        await RunAsync(MoveToItem(viewModel, halayaId, "Filling"));

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingRole!.Id.Should().Be(fillingRoleId);
        viewModel.Lines[0].Grams.Should().Be(250m);
        viewModel.History.IsDirty.Should().BeTrue("the grams are still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Lines[0].Grams.Should().Be(100m);
        viewModel.EditingRole!.Id.Should().Be(fillingRoleId, "no undo takes back a move that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task A_refused_move_says_why_and_moves_nothing()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 3);
        var glazeId = await AddRecipeAsync("Glaze", fillingRoleId);
        var ownGlazeId = await AddRecipeAsync("Cream Loaf Topping: Glaze", doughRoleId);
        var productId = await AddProductAsync("Cream Loaf");
        await SaveRecipeAsync(productId, new RecipeDraft(
            [],
            [
                new LinkedUsageDraft(glazeId, UsageAuthoredField.WeightPerPiece, 5m),
                new LinkedUsageDraft(ownGlazeId, UsageAuthoredField.WeightPerPiece, 5m),
            ]));
        var viewModel = await LoadedViewModelAsync();
        viewModel.SelectedTreeNode = viewModel.SubAssemblyTree.Find(glazeId);
        viewModel.SelectedTreeNodes.Add(viewModel.SubAssemblyTree.Find(ownGlazeId)!);

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(glazeId)).Single().Items.Single(i => i.Header == "Topping"));

        viewModel.ErrorMessage.Should().StartWith("Cream Loaf, which links this sub-assembly, has two Topping sub-assemblies called Glaze.");
        viewModel.MoveNotice.Should().BeNull();
        viewModel.SubAssemblyTree.Folder(toppingRoleId)!.SubAssemblies.Should().BeEmpty();
        (await RoleOfAsync(glazeId)).Should().Be(fillingRoleId);
    }

    [Fact]
    public async Task Rename_on_the_one_showing_saves_the_name_and_leaves_what_else_is_typed_to_save()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var viewModel = await OpenedViewModelAsync(halayaId);
        TextPromptRequest? asked = null;
        viewModel.PromptForName.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput("Ube Spread");
        });
        Edit(viewModel, () => viewModel.Lines[0].Grams = 250m);

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(halayaId)).Single(i => i.Header == "Rename…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked.Should().Be(new TextPromptRequest("Rename Sub-Assembly", "Name", "Ube Halaya"));
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.SubAssemblies.SingleAsync(s => s.Id == halayaId, Cancellation);
            saved.Name.Should().Be("Ube Spread");
            (await SubAssemblyTotalsAsync(halayaId)).TotalWeight.Should().Be(100m, "the rename saves the name alone");
        }

        viewModel.EditingName.Should().Be("Ube Spread");
        viewModel.HeaderTitle.Should().Be("Ube Spread");
        viewModel.SubAssemblyTree.Find(halayaId)!.Title.Should().Be("Ube Spread");
        viewModel.History.IsDirty.Should().BeTrue("the grams are still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingName.Should().Be("Ube Spread", "no undo takes back a rename that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task A_rename_to_a_name_in_use_is_refused_and_says_why()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        await AddRecipeAsync("Ube Jam", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput("ube jam"));

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(halayaId)).Single(i => i.Header == "Rename…"));

        viewModel.ErrorMessage.Should().NotBeNullOrEmpty();
        viewModel.SubAssemblyTree.Find(halayaId)!.Title.Should().Be("Ube Halaya");
    }

    [Fact]
    public async Task New_Here_starts_a_sub_assembly_in_the_folders_role_that_Save_files_there()
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var ubeId = await AddIngredientAsync("Ube");
        var viewModel = await LoadedViewModelAsync();

        Headers(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Folder(packagingRoleId))).Should().StartWith(["New Packaging Set Here"]);

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Folder(fillingRoleId)).Single(i => i.Header == "New Sub-Assembly Here"));

        viewModel.SelectedRow.Should().BeNull();
        viewModel.EditingRole!.Id.Should().Be(fillingRoleId);
        viewModel.History.IsDirty.Should().BeFalse("nothing is typed yet");

        Edit(viewModel, () => viewModel.EditingName = "Ube Halaya");
        Edit(viewModel, () => viewModel.Lines.Add(new IngredientLineRow(viewModel.Ingredients.Single(i => i.Id == ubeId), 100m)));
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.SubAssemblyTree.Folder(fillingRoleId)!.SubAssemblies.Select(l => l.Title).Should().Equal("Ube Halaya");
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.SubAssemblyTree.Find(viewModel.SelectedRow!.Id));
    }

    [Fact]
    public async Task The_folder_and_list_menus_add_sub_assemblies_and_roles_and_open_or_close_every_folder()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var viewModel = await LoadedViewModelAsync();
        var dough = viewModel.SubAssemblyTree.Folder(doughRoleId)!;

        Headers(viewModel.TreeMenuFor(dough))
            .Should().Equal("New Sub-Assembly Here", "New Role…", "Rename Role…", "Delete Role", "Expand All", "Collapse All");
        Headers(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Folder(packagingRoleId)))
            .Should().Equal(["New Packaging Set Here", "New Role…", "Rename Role…", "Expand All", "Collapse All"], "the Packaging role is never deleted");
        var listMenu = viewModel.TreeMenuFor(null);
        Headers(listMenu).Should().Equal("New Sub-Assembly", "New Role…", "Expand All", "Collapse All");

        await RunAsync(listMenu.Single(i => i.Header == "Collapse All"));
        viewModel.SubAssemblyTree.Roots.Should().OnlyContain(f => !f.IsExpanded);

        await RunAsync(viewModel.TreeMenuFor(dough).Single(i => i.Header == "Expand All"));
        viewModel.SubAssemblyTree.Roots.Should().OnlyContain(f => f.IsExpanded);
    }

    [Fact]
    public async Task New_Role_adds_a_folder_after_the_others_and_picks_it()
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var viewModel = await LoadedViewModelAsync();
        var answer = "Glaze";
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput(answer));

        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "New Role…"));

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.Roles.Select(r => r.Name).Should().Equal("Dough", "Glaze");
        viewModel.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough", "Glaze");
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.SubAssemblyTree.Roots[1]);

        answer = "dough";
        await RunAsync(viewModel.TreeMenuFor(null).Single(i => i.Header == "New Role…"));

        viewModel.Roles.Should().HaveCount(2, "a name that's a role already adds none");
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.SubAssemblyTree.Roots[0]);
    }

    [Fact]
    public async Task Rename_Role_renames_its_folder_keeps_it_product_owned_and_the_picker_follows()
    {
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 1, isProductOwned: true);
        var glazeId = await AddRecipeAsync("Glaze", toppingRoleId);
        var viewModel = await OpenedViewModelAsync(glazeId);
        viewModel.PromptForName.RegisterHandler(interaction => interaction.SetOutput("Toppings"));

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Folder(toppingRoleId)).Single(i => i.Header == "Rename Role…"));

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var role = await dbContext.SubAssemblyRoles.SingleAsync(Cancellation);
            (role.Name, role.IsProductOwned).Should().Be(("Toppings", true));
        }

        viewModel.SubAssemblyTree.Folder(toppingRoleId)!.Title.Should().Be("Toppings");
        viewModel.EditingRole!.Name.Should().Be("Toppings");
        viewModel.History.IsDirty.Should().BeFalse();
    }

    [Fact]
    public async Task Delete_Role_is_greyed_while_a_sub_assembly_is_in_it_and_deletes_an_empty_one_once_asked()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 2);
        await AddRecipeAsync("Pandesal Dough", doughRoleId);
        await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        // With a search on, the folder shows only one of them, but both keep the role.
        viewModel.SearchText = "Pandesal";
        var doughMenu = viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Folder(doughRoleId));
        doughMenu.Single(i => i.Header.StartsWith("Delete Role", StringComparison.Ordinal)).Should()
            .Match<TreeMenuItem>(i => i.Header == "Delete Role (holds 2)" && !i.IsEnabled);
        viewModel.SelectedTreeNode = viewModel.SubAssemblyTree.Folder(doughRoleId);
        viewModel.TreeShortcut("Del").Should().BeNull("Del does nothing where the menu's item is greyed");
        viewModel.SearchText = string.Empty;

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Folder(glazeRoleId)).Single(i => i.Header == "Delete Role"));

        viewModel.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Glaze' role? It holds no sub-assemblies here. This can't be undone.");
        viewModel.Roles.Select(r => r.Name).Should().Equal("Dough");
        viewModel.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough");
    }

    [Fact]
    public async Task Delete_on_a_sub_assembly_asks_first_and_the_form_starts_afresh_only_if_it_was_showing_it()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var viewModel = await OpenedViewModelAsync(pandesalId);
        string? question = null;
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(ensaymadaId)).Single(i => i.Header == "Delete"));

        question.Should().Be("Delete 'Ensaymada Dough'? This can't be undone.");
        viewModel.Rows.Select(r => r.Name).Should().Equal("Pandesal Dough");
        viewModel.SelectedRow!.Id.Should().Be(pandesalId, "the one showing wasn't the one deleted");

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(pandesalId)).Single(i => i.Header == "Delete"));

        viewModel.Rows.Should().BeEmpty();
        viewModel.SelectedRow.Should().BeNull();
        viewModel.EditingName.Should().BeEmpty();
    }

    [Fact]
    public async Task Make_In_House_Ingredient_makes_one_from_the_recipe_which_stays_on_the_list_and_lists_it_under_Used_By()
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var inHouseId = await MadeCategoryIdAsync();
        var viewModel = await OpenedViewModelAsync(custardId);
        ChoicePromptRequest? asked = null;
        viewModel.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(new ChoicePromptResult("Custard Filling", interaction.Input.InitialChoice));
        });

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(custardId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked!.Title.Should().Be("Make In-House Ingredient");
        asked.InitialValue.Should().Be("Custard", "the name starts as the sub-assembly's");
        asked.Choices[asked.InitialChoice].Should().Be("In-House");
        asked.Message.Should().Contain("Custard stays here as it is");
        viewModel.Rows.Should().ContainSingle(r => r.Id == custardId, "the recipe stays where it's written");
        viewModel.SuccessMessage.Should().Be("Custard Filling was added to In-House, made from Custard.");
        var made = viewModel.Ingredients.Should().ContainSingle(m => m.Name == "Custard Filling").Subject;
        made.Should().BeEquivalentTo(new { Source = RawMaterialSource.Made, CategoryId = inHouseId, WeightLossPercent = 0m, SubAssemblyId = (int?)custardId });
        viewModel.MadeIngredients.Should().Equal(new MadeIngredientUse(made.Id, "Custard Filling"));

        var opened = new List<int>();
        using var subscription = viewModel.RawMaterialOpenRequests.Subscribe(opened.Add);
        await viewModel.OpenRawMaterialCommand.Execute(made.Id);
        opened.Should().Equal(made.Id);
    }

    [Fact]
    public async Task Make_In_House_Ingredient_leaves_a_recipe_a_product_uses_and_the_product_as_they_are()
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var baseId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Ube Base", RoleId = fillingRoleId }, [(ubeId, 300m), (milkId, 100m)], Cancellation);
        var productId = await AddProductAsync("Ube Bun");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(baseId, UsageAuthoredField.WeightPerPiece, 20m)]));
        await MadeCategoryIdAsync();
        var viewModel = await OpenedViewModelAsync(baseId);
        string? message = null;
        viewModel.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            message = interaction.Input.Message;
            interaction.SetOutput(new ChoicePromptResult("Ube Filling", 0));
        });

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(baseId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().BeNull();
        message.Should().Contain("Ube Base stays here as it is");
        viewModel.SuccessMessage.Should().Be("Ube Filling was added to In-House, made from Ube Base.");
        viewModel.SelectedRow!.Id.Should().Be(baseId, "it stays on the list, and on the form");
        viewModel.Lines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal(("Ube", 300m), ("Milk", 100m));
        viewModel.BatchCost.Should().Be(70m, "₱60 of ube and ₱10 of milk, as before");
        viewModel.UsedBy.Select(p => p.Name).Should().Equal("Ube Bun");
        viewModel.MadeIngredients.Select(m => m.Name).Should().Equal("Ube Filling");
        viewModel.History.IsDirty.Should().BeFalse("the form shows it as it's saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductVersions.AnyAsync(v => v.ProductId == productId, Cancellation)).Should().BeFalse("nothing about Ube Bun changed");
    }

    [Fact]
    public async Task Make_In_House_Ingredient_waits_for_the_forms_changes_and_needs_a_Made_category()
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var flanId = await AddRecipeAsync("Leche Flan", fillingRoleId);
        var viewModel = await OpenedViewModelAsync(custardId);
        var asked = 0;
        viewModel.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked++;
            interaction.SetOutput(null);
        });
        Edit(viewModel, () => viewModel.EditingName = "Custard Base");

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(custardId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().Be("Save Custard first, or undo its changes: the made ingredient is made from it as it's saved.");

        await RunAsync(viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(flanId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().Be(MadeIngredientPrompt.NoMadeCategoryMessage);
        asked.Should().Be(0);
        viewModel.Rows.Should().HaveCount(2);
    }


    [Fact]
    public async Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 2);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        var pandesal = viewModel.SubAssemblyTree.Find(pandesalId)!;

        viewModel.SelectedTreeNode = pandesal;
        viewModel.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == viewModel.RenameSubAssemblyCommand && i.Parameter == pandesal);
        viewModel.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == viewModel.DeleteSubAssemblyCommand && i.Parameter == pandesal);

        viewModel.SelectedTreeNode = viewModel.SubAssemblyTree.Folder(glazeRoleId);
        viewModel.TreeShortcut("F2")!.Command.Should().BeSameAs(viewModel.RenameRoleCommand);
        viewModel.TreeShortcut("Del")!.Command.Should().BeSameAs(viewModel.DeleteRoleCommand);

        viewModel.SelectedTreeNode = pandesal;
        viewModel.SelectedTreeNodes.Add(viewModel.SubAssemblyTree.Find(ensaymadaId)!);
        viewModel.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    }

    [Fact]
    public async Task Picking_a_row_picks_its_sub_assembly_and_one_opened_from_elsewhere_is_picked_alone()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var spanishId = await AddRecipeAsync("Spanish Bread Dough", doughRoleId);
        var viewModel = await LoadedViewModelAsync();

        viewModel.SelectedTreeNode = viewModel.SubAssemblyTree.Find(pandesalId);
        viewModel.SelectedRow!.Id.Should().Be(pandesalId);

        viewModel.SelectedTreeNode = viewModel.SubAssemblyTree.Folder(doughRoleId);
        viewModel.SelectedRow!.Id.Should().Be(pandesalId, "a folder picked leaves the sub-assembly showing");

        viewModel.SelectedTreeNode = viewModel.SubAssemblyTree.Find(pandesalId);
        viewModel.SelectedTreeNodes.Add(viewModel.SubAssemblyTree.Find(ensaymadaId)!);
        viewModel.OpenSubAssembly(spanishId);

        viewModel.SelectedTreeNodes.Should().Equal(viewModel.SubAssemblyTree.Find(spanishId)!);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.SubAssemblyTree.Find(spanishId));
    }

    [Fact]
    public async Task The_folders_left_closed_stay_closed_next_time_and_a_new_role_opens()
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var store = new InMemoryViewStateStore();
        (await LoadedViewModelAsync(store)).Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedViewModelAsync(store);

        first.SubAssemblyTree.Folder(doughRoleId)!.IsExpanded = false;

        var doughKey = doughRoleId.ToString(CultureInfo.InvariantCulture);
        await WaitUntilSettledAsync(
            () => store.ReadList(SubAssembliesViewModel.ClosedFoldersKey)?.Contains(doughKey) == true,
            () => string.Join(", ", store.ReadList(SubAssembliesViewModel.ClosedFoldersKey) ?? []));
        first.Dispose();

        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 3);
        var next = await LoadedViewModelAsync(store);
        next.SubAssemblyTree.Folder(doughRoleId)!.IsExpanded.Should().BeFalse();
        next.SubAssemblyTree.Folder(fillingRoleId)!.IsExpanded.Should().BeTrue();
        next.SubAssemblyTree.Folder(glazeRoleId)!.IsExpanded.Should().BeTrue("a role new since opens, as every folder first did");

        // One closed just before the app closes is written as it closes.
        next.SubAssemblyTree.Folder(fillingRoleId)!.IsExpanded = false;
        next.Dispose();
        store.ReadList(SubAssembliesViewModel.ClosedFoldersKey).Should().Contain(fillingRoleId.ToString(CultureInfo.InvariantCulture));
    }

    private static IEnumerable<string> Headers(IEnumerable<TreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    /// <summary>The sub-assembly's menu's Move To item for <paramref name="role"/>.</summary>
    private static TreeMenuItem MoveToItem(SubAssembliesViewModel viewModel, int subAssemblyId, string role) =>
        viewModel.TreeMenuFor(viewModel.SubAssemblyTree.Find(subAssemblyId)).Single(i => i.Header == "Move to").Items.Single(i => i.Header == role);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    [Fact]
    public async Task Picking_another_sub_assembly_over_unsaved_changes_asks_and_Cancel_stays_on_them()
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var plainId = await AddRecipeAsync("Plain Dough", doughId);
        var viewModel = await OpenedViewModelAsync(sweetId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Sweeter Dough";

        Click(viewModel, viewModel.SubAssemblyTree.Find(plainId)!);
        await WaitUntilSettledAsync(() => asked.Count == 1, () => $"asked={asked.Count}");

        asked.Single().Should().StartWith("Save the changes to Sweeter Dough?");
        viewModel.SelectedRow!.Id.Should().Be(sweetId);
        viewModel.SelectedTreeNode.Should().BeSameAs(viewModel.SubAssemblyTree.Find(sweetId), "the list goes back to the row showing");
        viewModel.EditingName.Should().Be("Sweeter Dough");
        viewModel.History.IsDirty.Should().BeTrue();
    }

    [Theory]
    [InlineData(UnsavedChangesAnswer.Save, "Sweeter Dough")]
    [InlineData(UnsavedChangesAnswer.DontSave, "Sweet Dough")]
    public async Task Picking_another_sub_assembly_over_unsaved_changes_opens_it_once_they_are_saved_or_not(UnsavedChangesAnswer answer, string savedName)
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var plainId = await AddRecipeAsync("Plain Dough", doughId);
        var viewModel = await OpenedViewModelAsync(sweetId);
        AnswerUnsavedChanges(viewModel, answer);
        viewModel.EditingName = "Sweeter Dough";

        Click(viewModel, viewModel.SubAssemblyTree.Find(plainId)!);
        await WaitUntilSettledAsync(
            () => viewModel.SelectedRow?.Id == plainId && viewModel.EditingName == "Plain Dough" && !viewModel.History.IsDirty,
            () => $"selected={viewModel.SelectedRow?.Id} name={viewModel.EditingName}");

        viewModel.ErrorMessage.Should().BeNull();
        (await NameOfAsync(sweetId)).Should().Be(savedName);
    }

    [Fact]
    public async Task New_over_unsaved_changes_asks_and_Cancel_keeps_them()
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var viewModel = await OpenedViewModelAsync(sweetId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Sweeter Dough";

        await viewModel.NewCommand.Execute();

        asked.Should().ContainSingle();
        viewModel.SelectedRow!.Id.Should().Be(sweetId);
        viewModel.EditingName.Should().Be("Sweeter Dough");
    }

    [Fact]
    public async Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved()
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var viewModel = await OpenedViewModelAsync(sweetId);
        AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        viewModel.EditingName = "Sweeter Dough";

        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();

        await WaitUntilSettledAsync(() => viewModel.EditingName == "Sweet Dough" && !viewModel.History.IsDirty, () => $"name={viewModel.EditingName}");
        viewModel.SelectedRow!.Id.Should().Be(sweetId);
        (await NameOfAsync(sweetId)).Should().Be("Sweet Dough");
    }

    // No puts a new sub-assembly back blank. What's typed into it next has to count, or it's lost with no question asked.
    [Fact]
    public async Task What_is_typed_after_no_on_a_new_sub_assembly_counts_as_unsaved()
    {
        var viewModel = await LoadedViewModelAsync();
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        viewModel.EditingName = "Sweet Dough";
        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        viewModel.EditingName.Should().BeEmpty();
        viewModel.History.IsDirty.Should().BeFalse();

        viewModel.EditingName = "Sweeter Dough";

        viewModel.History.IsDirty.Should().BeTrue();
        await viewModel.ResolveUnsavedChangesAsync(revert: false);
        asked.Should().HaveCount(2);
    }

    /// <summary>Answers the question about unsaved changes with <paramref name="answer"/>, collecting each question asked.</summary>
    private static List<string> AnswerUnsavedChanges(SubAssembliesViewModel viewModel, UnsavedChangesAnswer answer)
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
    private static void Click(SubAssembliesViewModel viewModel, object row)
    {
        viewModel.SelectedTreeNodes.Clear();
        viewModel.SelectedTreeNodes.Add(row);
    }

    private async Task<string> NameOfAsync(int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).Select(s => s.Name).SingleAsync(Cancellation);
    }

    private static async Task RunAsync(TreeMenuItem item)
    {
        switch (item.Command)
        {
            case ReactiveCommand<IReadOnlyList<SubAssemblyMove>, RxVoid> move:
                await move.Execute((IReadOnlyList<SubAssemblyMove>)item.Parameter!);
                break;
            case ReactiveCommand<SubAssemblyLeafNode, RxVoid> onSubAssembly:
                await onSubAssembly.Execute((SubAssemblyLeafNode)item.Parameter!);
                break;
            case ReactiveCommand<SubAssemblyFolderNode, RxVoid> onFolder:
                // New Sub-Assembly, on the list's empty space, has no folder.
                await onFolder.Execute((SubAssemblyFolderNode)item.Parameter!);
                break;
            case ReactiveCommand<RxVoid, RxVoid> plain:
                await plain.Execute();
                break;
            default:
                throw new InvalidOperationException($"'{item.Header}' runs nothing a test knows how to wait for.");
        }
    }

    /// <summary>One finished change, as the Controls/ behaviours bracket a gesture in the app.</summary>
    private static void Edit(SubAssembliesViewModel viewModel, Action change)
    {
        var recorder = (IUndoRecorder)viewModel;
        recorder.BeginUndoStep();
        change();
        recorder.EndUndoStep();
    }

    /// <summary>A recipe of 100 g of one ingredient, in <paramref name="roleId"/>.</summary>
    private async Task<int> AddRecipeAsync(string name, int roleId)
    {
        var ingredientId = await AddIngredientAsync($"{name} Ingredient");
        return await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = name, RoleId = roleId }, [(ingredientId, 100m)], Cancellation);
    }

    /// <summary>A packaging set of one box, in the Packaging role <paramref name="roleId"/>.</summary>
    private async Task<int> AddPackagingSetAsync(string name, int roleId)
    {
        var pieceId = await GetOrCreateUnitOfMeasureAsync("piece", "pc");
        int boxId;
        await using (var dbContext = CreateDbContext())
        {
            var box = new RawMaterial
            {
                Type = RawMaterialType.Packaging,
                Name = $"{name} Box",
                Category = new RawMaterialCategory { Name = $"{name} Boxes", Type = RawMaterialCategoryType.Packaging },
                BaseUnitId = pieceId,
            };
            dbContext.RawMaterials.Add(box);
            await dbContext.SaveChangesAsync(Cancellation);
            boxId = box.Id;
        }

        return await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandalonePackagingAsync(
            new SubAssembly { Name = name, RoleId = roleId }, [(boxId, null, 1m)], Cancellation);
    }

    private async Task<int> RoleOfAsync(int subAssemblyId)
    {
        await using var dbContext = CreateDbContext();
        return await dbContext.SubAssemblies.Where(s => s.Id == subAssemblyId).Select(s => s.RoleId).SingleAsync(Cancellation);
    }

    private async Task<SubAssembliesViewModel> LoadedViewModelAsync(IViewStateStore? viewStateStore = null)
    {
        var viewModel = CreateViewModel(viewStateStore);
        await viewModel.LoadCommand.Execute();
        return viewModel;
    }

    /// <summary>The screen with <paramref name="subAssemblyId"/> open on the form, its lines in.</summary>
    private async Task<SubAssembliesViewModel> OpenedViewModelAsync(int subAssemblyId)
    {
        var viewModel = await LoadedViewModelAsync();
        viewModel.OpenSubAssembly(subAssemblyId);
        await WaitUntilSettledAsync(
            () => viewModel.SelectedRow?.Id == subAssemblyId && viewModel.Lines.Count > 0 && !viewModel.History.IsDirty,
            () => $"lines={viewModel.Lines.Count}");
        return viewModel;
    }

    private SubAssembliesViewModel CreateViewModel(IViewStateStore? viewStateStore)
    {
        ReactiveUIBootstrapper.EnsureInitialized();
        var factory = CreateDbContextFactory();

        return new SubAssembliesViewModel(
            new SubAssemblyRepository(factory),
            new SubAssemblyLineRepository(factory),
            new SubAssemblyRoleRepository(factory),
            new RawMaterialRepository(factory),
            new RawMaterialCategoryRepository(factory),
            new SubAssemblyValidator(),
            new SubAssemblyRoleValidator(),
            viewStateStore ?? new InMemoryViewStateStore(),
            VersionRecorders.CreateSaves(factory, CreateUnitOfWork()),
            new LoggerConfiguration().CreateLogger());
    }

    /// <summary>
    /// Waits until <paramref name="condition"/> has held for a moment: a sub-assembly's lines load in the background, and
    /// the closed folders are written once they settle, with nothing to await.
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
