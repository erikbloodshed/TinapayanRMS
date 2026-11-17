using System.Globalization;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Validators;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Infrastructure.Repositories;
using TinapayanRMS.Tests.Common.Fixtures;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>
/// The Sub-Assemblies list's right-click menus, its keys and its selection, as RawMaterialsStateTreeTests has them for
/// the Raw Materials list: what each menu offers a sub-assembly, several sub-assemblies, a role's folder and the list
/// itself, and what each item does, run as the menu runs it. A recipe only moves between recipe roles, and a packaging set
/// stays in the Packaging role.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class SubAssembliesStateTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_sub_assemblys_menu_moves_it_to_another_recipe_role_and_offers_rename_and_delete() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var doughId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var state = await LoadedStateAsync();
        var leaf = state.List.SubAssemblyTree.Find(doughId)!;
        state.List.SelectedTreeNode = leaf;

        var menu = state.List.TreeMenuFor(leaf);

        Headers(menu).Should().Equal("Move to", "Rename", "Make In-House Ingredient…", "Delete");
        menu.Single(i => i.Header == "Rename").Gesture.Should().Be("F2");
        menu.Single(i => i.Header == "Delete").Gesture.Should().Be("Del");
        var moveTo = menu.Single(i => i.Header == "Move to").Items;
        moveTo.Select(i => i.Header).Should().Equal(["Dough", "Filling"], "only the recipe roles, in their order");
        moveTo.Single(i => i.Header == "Dough").IsEnabled.Should().BeFalse("it's in that folder already");
    });

    [Test]
    public Task A_packaging_set_has_nowhere_to_move_and_nor_has_one_picked_with_a_recipe() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var doughId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var setId = await AddPackagingSetAsync("Gift Set", packagingRoleId);
        var state = await LoadedStateAsync();
        var set = state.List.SubAssemblyTree.Find(setId)!;

        Headers(state.List.TreeMenuFor(set)).Should().Equal("Rename", "Delete");

        state.List.SelectedTreeNode = state.List.SubAssemblyTree.Find(doughId);
        state.List.SelectedTreeNodes.Add(set);

        state.List.TreeMenuFor(set).Should().BeEmpty();
    });

    [Test]
    public Task A_drop_moves_recipes_onto_another_roles_folder_and_gives_the_first_back_to_bring_into_view() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var state = await LoadedStateAsync();
        IListTreeScreen screen = state.List;
        var halaya = state.List.SubAssemblyTree.Find(halayaId)!;
        var filling = state.List.SubAssemblyTree.Folder(fillingRoleId)!;

        screen.CanDrag(halaya).Should().BeTrue();
        screen.CanDrag(filling).Should().BeFalse("a folder isn't dragged");
        screen.CanDrop([halaya], state.List.SubAssemblyTree.Folder(doughRoleId)).Should().BeFalse("it's there already");

        (await screen.DropAsync([halaya], filling)).Should().BeSameAs(halaya);

        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        filling.SubAssemblies.Should().ContainSingle();
        state.List.MoveNotice.Should().Be("Moved Ube Halaya to Filling");
    });

    [Test]
    public Task A_drop_made_while_a_move_is_still_saving_does_nothing() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var yemaId = await AddRecipeAsync("Yema", doughRoleId);
        var state = await LoadedStateAsync();
        IListTreeScreen screen = state.List;

        var firstMove = state.List.MoveSubAssembliesCommand.ExecuteAsync([new SubAssemblyMove(halayaId, fillingRoleId)]);
        state.List.MoveSubAssembliesCommand.CanExecute([]).Should().BeFalse("the move is under way, as a drop would find it");
        (await screen.DropAsync([state.List.SubAssemblyTree.Find(yemaId)!], state.List.SubAssemblyTree.Folder(fillingRoleId)))
            .Should().BeNull("a move is still saving");
        await firstMove;

        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        (await RoleOfAsync(yemaId)).Should().Be(doughRoleId, "the drop made meanwhile did nothing");
    });

    [Test]
    public Task Move_to_saves_at_once_says_so_and_Undo_on_the_notice_puts_it_back() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var state = await LoadedStateAsync();

        await RunAsync(MoveToItem(state, halayaId, "Filling"));

        state.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        state.List.SubAssemblyTree.Folder(fillingRoleId)!.SubAssemblies.Should().ContainSingle();
        state.List.MoveNotice.Should().Be("Moved Ube Halaya to Filling");

        await state.List.UndoMoveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(doughRoleId);
        state.List.SubAssemblyTree.Folder(doughRoleId)!.SubAssemblies.Should().ContainSingle();
        state.List.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        state.List.UndoMoveCommand.CanExecute().Should().BeFalse();
    });

    [Test]
    public Task Several_picked_move_together_and_the_one_showing_stays_on_the_form() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var jamId = await AddRecipeAsync("Ube Jam", doughRoleId);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var state = await OpenedStateAsync(halayaId);
        var tree = state.List.SubAssemblyTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        state.List.SelectedTreeNodes.Add(tree.Find(jamId)!);
        state.List.SelectedTreeNodes.Add(tree.Find(custardId)!);
        state.SelectedRow!.Id.Should().Be(halayaId);

        var menu = state.List.TreeMenuFor(tree.Find(jamId));

        Headers(menu).Should().Equal(["Move 3 Sub-Assemblies to"], "renaming or deleting is for one at a time");
        await RunAsync(menu[0].Items.Single(i => i.Header == "Filling"));

        state.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        (await RoleOfAsync(jamId)).Should().Be(fillingRoleId);
        state.List.MoveNotice.Should().Be("Moved 2 sub-assemblies to Filling", "Custard was there already");
        state.List.SelectedTreeSubAssemblies.Select(l => l.Row.Id).Should().Equal(halayaId, jamId, custardId);
        state.SelectedRow!.Id.Should().Be(halayaId);
    });

    [Test]
    public Task A_move_of_the_one_showing_moves_its_Role_picker_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var state = await OpenedStateAsync(halayaId);
        Edit(state, () => state.Batch.Lines[0].Grams = 250m);

        await RunAsync(MoveToItem(state, halayaId, "Filling"));

        state.ErrorMessage.Should().BeNull();
        state.EditingRole!.Id.Should().Be(fillingRoleId);
        state.Batch.Lines[0].Grams.Should().Be(250m);
        state.History.IsDirty.Should().BeTrue("the grams are still to be saved");

        state.TryUndo().Should().BeTrue();

        state.Batch.Lines[0].Grams.Should().Be(100m);
        state.EditingRole!.Id.Should().Be(fillingRoleId, "no undo takes back a move that's saved");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_refused_move_says_why_and_moves_nothing() => UiThread.RunAsync(async () =>
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
        var state = await LoadedStateAsync();
        state.List.SelectedTreeNode = state.List.SubAssemblyTree.Find(glazeId);
        state.List.SelectedTreeNodes.Add(state.List.SubAssemblyTree.Find(ownGlazeId)!);

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(glazeId)).Single().Items.Single(i => i.Header == "Topping"));

        state.ErrorMessage.Should().StartWith("Cream Loaf, which links this sub-assembly, has two Topping sub-assemblies called Glaze.");
        state.List.MoveNotice.Should().BeNull();
        state.List.SubAssemblyTree.Folder(toppingRoleId)!.SubAssemblies.Should().BeEmpty();
        (await RoleOfAsync(glazeId)).Should().Be(fillingRoleId);
    });

    [Test]
    public Task Rename_types_the_name_in_place_saves_it_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var state = await OpenedStateAsync(halayaId);
        Edit(state, () => state.Batch.Lines[0].Grams = 250m);
        var halaya = state.List.SubAssemblyTree.Find(halayaId)!;

        await RunAsync(state.List.TreeMenuFor(halaya).Single(i => i.Header == "Rename"));

        state.List.TreeNameEdit.Should().Be(new TreeNameEdit(halaya, "Ube Halaya"));

        await state.List.CommitTreeNameAsync(" Ube Spread ");
        await state.List.CommitTreeNameAsync("Ube Spread Again");

        state.ErrorMessage.Should().BeNull();
        state.List.TreeNameEdit.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.SubAssemblies.SingleAsync(s => s.Id == halayaId, Cancellation);
            saved.Name.Should().Be("Ube Spread", "leaving the box after Enter saves nothing more");
            (await SubAssemblyTotalsAsync(halayaId)).TotalWeight.Should().Be(100m, "the rename saves the name alone");
        }

        state.EditingName.Should().Be("Ube Spread");
        state.HeaderTitle.Should().Be("Ube Spread");
        state.List.SubAssemblyTree.Find(halayaId)!.Title.Should().Be("Ube Spread");
        state.History.IsDirty.Should().BeTrue("the grams are still to be saved");

        state.TryUndo().Should().BeTrue();

        state.EditingName.Should().Be("Ube Spread", "no undo takes back a rename that's saved");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_rename_to_a_name_in_use_is_refused_says_why_and_brings_the_box_back() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        await AddRecipeAsync("Ube Jam", doughRoleId);
        var state = await LoadedStateAsync();
        var halaya = state.List.SubAssemblyTree.Find(halayaId)!;

        await RunAsync(state.List.TreeMenuFor(halaya).Single(i => i.Header == "Rename"));
        await state.List.CommitTreeNameAsync("ube jam");

        state.ErrorMessage.Should().NotBeNullOrEmpty();
        state.List.SubAssemblyTree.Find(halayaId)!.Title.Should().Be("Ube Halaya");
        state.List.TreeNameEdit.Should().Be(new TreeNameEdit(halaya, "ube jam"));

        // Esc, an empty name and the same name leave it be; so does a search.
        state.List.CancelTreeName();
        state.List.SelectedTreeNode = halaya;
        await RunAsync(state.List.TreeShortcut("F2")!);
        await state.List.CommitTreeNameAsync(" ");
        await RunAsync(state.List.TreeShortcut("F2")!);
        await state.List.CommitTreeNameAsync("Ube Halaya");
        await RunAsync(state.List.TreeShortcut("F2")!);
        state.List.SearchText = "ube";
        state.List.TreeNameEdit.Should().BeNull();
        (await NameOfAsync(halayaId)).Should().Be("Ube Halaya");
    });

    [Test]
    public Task New_names_the_sub_assembly_in_its_roles_folder_and_the_row_stays_until_Save_files_it() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var ubeId = await AddIngredientAsync("Ube");
        var state = await LoadedStateAsync();
        var filling = state.List.SubAssemblyTree.Folder(fillingRoleId)!;
        state.List.SearchText = "nothing like it";

        await RunAsync(state.List.TreeMenuFor(filling).Single(i => i.Header == "New Sub-Assembly Here"));

        state.List.SearchText.Should().BeEmpty("the search is cleared, so the new row shows");
        var row = state.List.SubAssemblyTree.PendingSubAssembly!;
        filling.Children.Should().Equal(row);
        filling.Count.Should().Be(0, "it isn't a sub-assembly until it's saved");
        state.List.SelectedTreeNode.Should().BeSameAs(row);
        state.List.TreeNameEdit.Should().Be(new TreeNameEdit(row, "New sub-assembly"));
        state.List.TreeMenuFor(row).Should().BeEmpty();
        state.List.SelectedTreeSubAssemblies.Should().BeEmpty();
        state.EditingRole!.Id.Should().Be(fillingRoleId);

        await state.List.CommitTreeNameAsync("Ube Halaya");

        state.ErrorMessage.Should().BeNull();
        state.List.TreeNameEdit.Should().BeNull();
        row.Title.Should().Be("Ube Halaya");
        state.EditingName.Should().Be("Ube Halaya");
        state.History.IsDirty.Should().BeTrue("Save is offered for it");

        await state.LoadCommand.ExecuteAsync();
        filling.Children.Should().Equal([row], "a load keeps it");

        Edit(state, () => state.Batch.Lines.Add(new IngredientLineRow(state.Ingredients.Single(i => i.Id == ubeId), 100m)));
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.List.SubAssemblyTree.PendingSubAssembly.Should().BeNull();
        filling.SubAssemblies.Select(l => l.Title).Should().Equal("Ube Halaya");
        state.List.SelectedTreeNode.Should().BeSameAs(state.List.SubAssemblyTree.Find(state.SelectedRow!.Id));

        // In the Packaging role, a packaging set; Esc takes its row away, and Copy the form's.
        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Folder(packagingRoleId)).Single(i => i.Header == "New Packaging Set Here"));
        state.List.TreeNameEdit!.InitialText.Should().Be("New packaging set");
        state.List.CancelTreeName();
        state.List.SubAssemblyTree.Folder(packagingRoleId)!.Children.Should().BeEmpty();
        state.List.SelectedTreeNode.Should().BeNull();
    });

    [Test]
    public Task New_from_the_header_names_the_sub_assembly_in_the_first_role_and_Dont_Save_takes_the_row_away() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        var state = await LoadedStateAsync();
        AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        var dough = state.List.SubAssemblyTree.Folder(doughRoleId)!;

        await state.NewCommand.ExecuteAsync();
        await state.List.CommitTreeNameAsync("Sweet Dough");

        dough.Children.Select(c => c.Title).Should().Equal("Sweet Dough");

        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("Don't Save puts the form back blank");

        state.List.SubAssemblyTree.PendingSubAssembly.Should().BeNull("put back blank, it has no row");
        dough.Children.Should().BeEmpty();
        state.List.Rows.Should().BeEmpty();
    });

    [Test]
    public Task New_Here_starts_a_sub_assembly_in_the_folders_role_that_Save_files_there() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var ubeId = await AddIngredientAsync("Ube");
        var state = await LoadedStateAsync();

        Headers(state.List.TreeMenuFor(state.List.SubAssemblyTree.Folder(packagingRoleId))).Should().StartWith(["New Packaging Set Here"]);

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Folder(fillingRoleId)).Single(i => i.Header == "New Sub-Assembly Here"));

        state.SelectedRow.Should().BeNull();
        state.EditingRole!.Id.Should().Be(fillingRoleId);
        state.History.IsDirty.Should().BeFalse("nothing is typed yet");

        Edit(state, () => state.EditingName = "Ube Halaya");
        Edit(state, () => state.Batch.Lines.Add(new IngredientLineRow(state.Ingredients.Single(i => i.Id == ubeId), 100m)));
        await state.SaveCommand.ExecuteAsync();

        state.ErrorMessage.Should().BeNull();
        state.List.SubAssemblyTree.Folder(fillingRoleId)!.SubAssemblies.Select(l => l.Title).Should().Equal("Ube Halaya");
        state.List.SelectedTreeNode.Should().BeSameAs(state.List.SubAssemblyTree.Find(state.SelectedRow!.Id));
    });

    [Test]
    public Task The_folder_and_list_menus_add_sub_assemblies_and_roles_and_open_or_close_every_folder() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var state = await LoadedStateAsync();
        var dough = state.List.SubAssemblyTree.Folder(doughRoleId)!;

        Headers(state.List.TreeMenuFor(dough))
            .Should().Equal("New Sub-Assembly Here", "New Folder", "Rename Role", "Delete Role", "Expand All", "Collapse All");
        Headers(state.List.TreeMenuFor(state.List.SubAssemblyTree.Folder(packagingRoleId)))
            .Should().Equal(["New Packaging Set Here", "New Folder", "Rename Role", "Expand All", "Collapse All"], "the Packaging role is never deleted");
        var listMenu = state.List.TreeMenuFor(null);
        Headers(listMenu).Should().Equal("New Sub-Assembly", "New Folder", "Expand All", "Collapse All");

        await RunAsync(listMenu.Single(i => i.Header == "Collapse All"));
        state.List.SubAssemblyTree.Roots.Should().OnlyContain(f => !f.IsExpanded);

        await RunAsync(state.List.TreeMenuFor(dough).Single(i => i.Header == "Expand All"));
        state.List.SubAssemblyTree.Roots.Should().OnlyContain(f => f.IsExpanded);
    });

    [Test]
    public Task New_Folder_names_a_role_in_place_after_the_others_and_picks_it() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var state = await LoadedStateAsync();

        await RunAsync(state.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));

        var folder = state.List.SubAssemblyTree.PendingFolder!;
        state.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough", "New folder");
        state.List.SelectedTreeNode.Should().BeSameAs(folder);
        state.List.TreeNameEdit!.Node.Should().BeSameAs(folder);
        state.List.TreeNameEdit.Hint.Should().Contain("a section in the recipes that use it");
        state.List.TreeMenuFor(folder).Should().BeEmpty();

        await state.LoadCommand.ExecuteAsync();
        state.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal(["Dough", "New folder"], "a load keeps it");

        await state.List.CommitTreeNameAsync("Glaze");

        state.ErrorMessage.Should().BeNull();
        state.List.SubAssemblyTree.PendingFolder.Should().BeNull();
        state.Roles.Select(r => r.Name).Should().Equal("Dough", "Glaze");
        state.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough", "Glaze");
        state.List.SelectedTreeNode.Should().BeSameAs(state.List.SubAssemblyTree.Roots[1]);

        await RunAsync(state.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        await state.List.CommitTreeNameAsync("dough");

        state.Roles.Should().HaveCount(2, "a name that's a role already adds none");
        state.List.SelectedTreeNode.Should().BeSameAs(state.List.SubAssemblyTree.Roots[0]);

        // Esc and an empty name add nothing.
        await RunAsync(state.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        state.List.CancelTreeName();
        await RunAsync(state.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        await state.List.CommitTreeNameAsync(string.Empty);
        state.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough", "Glaze");
        state.Roles.Should().HaveCount(2);
    });

    [Test]
    public Task Rename_Role_types_the_name_in_place_keeps_it_product_owned_and_the_picker_follows() => UiThread.RunAsync(async () =>
    {
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 1, isProductOwned: true);
        var glazeId = await AddRecipeAsync("Glaze", toppingRoleId);
        var state = await OpenedStateAsync(glazeId);
        var topping = state.List.SubAssemblyTree.Folder(toppingRoleId)!;

        await RunAsync(state.List.TreeMenuFor(topping).Single(i => i.Header == "Rename Role"));

        state.List.TreeNameEdit!.Node.Should().BeSameAs(topping);
        state.List.TreeNameEdit.InitialText.Should().Be("Topping");
        await state.List.CommitTreeNameAsync("Toppings");

        state.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var role = await dbContext.SubAssemblyRoles.SingleAsync(Cancellation);
            (role.Name, role.IsProductOwned).Should().Be(("Toppings", true));
        }

        state.List.SubAssemblyTree.Folder(toppingRoleId)!.Title.Should().Be("Toppings");
        state.EditingRole!.Name.Should().Be("Toppings");
        state.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Delete_Role_is_greyed_while_a_sub_assembly_is_in_it_and_deletes_an_empty_one_once_asked() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 2);
        await AddRecipeAsync("Pandesal Dough", doughRoleId);
        await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var state = await LoadedStateAsync();
        string? question = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        // With a search on, the folder shows only one of them, but both keep the role.
        state.List.SearchText = "Pandesal";
        var doughMenu = state.List.TreeMenuFor(state.List.SubAssemblyTree.Folder(doughRoleId));
        doughMenu.Single(i => i.Header.StartsWith("Delete Role", StringComparison.Ordinal)).Should()
            .Match<TreeMenuItem>(i => i.Header == "Delete Role (holds 2)" && !i.IsEnabled);
        state.List.SelectedTreeNode = state.List.SubAssemblyTree.Folder(doughRoleId);
        state.List.TreeShortcut("Del").Should().BeNull("Del does nothing where the menu's item is greyed");
        state.List.SearchText = string.Empty;

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Folder(glazeRoleId)).Single(i => i.Header == "Delete Role"));

        state.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Glaze' role? It holds no sub-assemblies here. This can't be undone.");
        state.Roles.Select(r => r.Name).Should().Equal("Dough");
        state.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough");
    });

    [Test]
    public Task Delete_on_a_sub_assembly_asks_first_and_the_form_starts_afresh_only_if_it_was_showing_it() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var state = await OpenedStateAsync(pandesalId);
        string? question = null;
        state.ConfirmDelete.RegisterHandler(interaction =>
        {
            question = interaction.Input;
            interaction.SetOutput(true);
        });

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(ensaymadaId)).Single(i => i.Header == "Delete"));

        question.Should().Be("Delete 'Ensaymada Dough'? This can't be undone.");
        state.List.Rows.Select(r => r.Name).Should().Equal("Pandesal Dough");
        state.SelectedRow!.Id.Should().Be(pandesalId, "the one showing wasn't the one deleted");

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(pandesalId)).Single(i => i.Header == "Delete"));

        state.List.Rows.Should().BeEmpty();
        state.SelectedRow.Should().BeNull();
        state.EditingName.Should().BeEmpty();
    });

    [Test]
    public Task Make_In_House_Ingredient_makes_one_from_the_recipe_which_stays_on_the_list_and_lists_it_under_Used_By() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var inHouseId = await MadeCategoryIdAsync();
        var state = await OpenedStateAsync(custardId);
        ChoicePromptRequest? asked = null;
        state.List.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(new ChoicePromptResult("Custard Filling", interaction.Input.InitialChoice));
        });

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(custardId)).Single(i => i.Header == "Make In-House Ingredient…"));

        state.ErrorMessage.Should().BeNull();
        asked!.Title.Should().Be("Make In-House Ingredient");
        asked.InitialValue.Should().Be("Custard", "the name starts as the sub-assembly's");
        asked.Choices[asked.InitialChoice].Should().Be("In-House");
        asked.Message.Should().Contain("Custard stays here as it is");
        state.List.Rows.Should().ContainSingle(r => r.Id == custardId, "the recipe stays where it's written");
        state.SuccessMessage.Should().Be("Custard Filling was added to In-House, made from Custard.");
        var made = state.Ingredients.Should().ContainSingle(m => m.Name == "Custard Filling").Subject;
        made.Should().BeEquivalentTo(new { Source = RawMaterialSource.Made, CategoryId = inHouseId, WeightLossPercent = 0m, SubAssemblyId = (int?)custardId });
        state.Dependents.MadeIngredients.Should().Equal(new MadeIngredientUse(made.Id, "Custard Filling"));

        var opened = new List<int>();
        using var subscription = state.Dependents.RawMaterialOpenRequests.Subscribe(opened.Add);
        await state.Dependents.OpenRawMaterialCommand.ExecuteAsync(made.Id);
        opened.Should().Equal(made.Id);
    });

    [Test]
    public Task Make_In_House_Ingredient_leaves_a_recipe_a_product_uses_and_the_product_as_they_are() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var ubeId = await AddIngredientAsync("Ube", costPerGram: 0.2m);
        var milkId = await AddIngredientAsync("Milk", costPerGram: 0.1m);
        var baseId = await new SubAssemblyRepository(CreateDbContextFactory()).SaveStandaloneAsync(
            new SubAssembly { Name = "Ube Base", RoleId = fillingRoleId }, [(ubeId, 300m), (milkId, 100m)], Cancellation);
        var productId = await AddProductAsync("Ube Bun");
        await SaveRecipeAsync(productId, new RecipeDraft([], [new LinkedUsageDraft(baseId, UsageAuthoredField.WeightPerPiece, 20m)]));
        await MadeCategoryIdAsync();
        var state = await OpenedStateAsync(baseId);
        string? message = null;
        state.List.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            message = interaction.Input.Message;
            interaction.SetOutput(new ChoicePromptResult("Ube Filling", 0));
        });

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(baseId)).Single(i => i.Header == "Make In-House Ingredient…"));

        state.ErrorMessage.Should().BeNull();
        message.Should().Contain("Ube Base stays here as it is");
        state.SuccessMessage.Should().Be("Ube Filling was added to In-House, made from Ube Base.");
        state.SelectedRow!.Id.Should().Be(baseId, "it stays on the list, and on the form");
        state.Batch.Lines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal(("Ube", 300m), ("Milk", 100m));
        state.Batch.BatchCost.Should().Be(70m, "₱60 of ube and ₱10 of milk, as before");
        state.Dependents.Products.Select(p => p.Name).Should().Equal("Ube Bun");
        state.Dependents.MadeIngredients.Select(m => m.Name).Should().Equal("Ube Filling");
        state.History.IsDirty.Should().BeFalse("the form shows it as it's saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductVersions.AnyAsync(v => v.ProductId == productId, Cancellation)).Should().BeFalse("nothing about Ube Bun changed");
    });

    [Test]
    public Task Make_In_House_Ingredient_waits_for_the_forms_changes_and_needs_a_Made_category() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var flanId = await AddRecipeAsync("Leche Flan", fillingRoleId);
        var state = await OpenedStateAsync(custardId);
        var asked = 0;
        state.List.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked++;
            interaction.SetOutput(null);
        });
        Edit(state, () => state.EditingName = "Custard Base");

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(custardId)).Single(i => i.Header == "Make In-House Ingredient…"));

        state.ErrorMessage.Should().Be("Save Custard first, or undo its changes: the made ingredient is made from it as it's saved.");

        await RunAsync(state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(flanId)).Single(i => i.Header == "Make In-House Ingredient…"));

        state.ErrorMessage.Should().Be(MadeIngredientPrompt.NoMadeCategoryMessage);
        asked.Should().Be(0);
        state.List.Rows.Should().HaveCount(2);
    });


    [Test]
    public Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 2);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var state = await LoadedStateAsync();
        var pandesal = state.List.SubAssemblyTree.Find(pandesalId)!;

        state.List.SelectedTreeNode = pandesal;
        state.List.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == state.List.RenameSubAssemblyCommand && i.Parameter == pandesal);
        state.List.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == state.List.DeleteSubAssemblyCommand && i.Parameter == pandesal);

        state.List.SelectedTreeNode = state.List.SubAssemblyTree.Folder(glazeRoleId);
        state.List.TreeShortcut("F2")!.Command.Should().BeSameAs(state.List.RenameRoleCommand);
        state.List.TreeShortcut("Del")!.Command.Should().BeSameAs(state.List.DeleteRoleCommand);

        state.List.SelectedTreeNode = pandesal;
        state.List.SelectedTreeNodes.Add(state.List.SubAssemblyTree.Find(ensaymadaId)!);
        state.List.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    });

    [Test]
    public Task Picking_a_row_picks_its_sub_assembly_and_one_opened_from_elsewhere_is_picked_alone() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var spanishId = await AddRecipeAsync("Spanish Bread Dough", doughRoleId);
        var state = await LoadedStateAsync();

        state.List.SelectedTreeNode = state.List.SubAssemblyTree.Find(pandesalId);
        state.SelectedRow!.Id.Should().Be(pandesalId);

        state.List.SelectedTreeNode = state.List.SubAssemblyTree.Folder(doughRoleId);
        state.SelectedRow!.Id.Should().Be(pandesalId, "a folder picked leaves the sub-assembly showing");

        state.List.SelectedTreeNode = state.List.SubAssemblyTree.Find(pandesalId);
        state.List.SelectedTreeNodes.Add(state.List.SubAssemblyTree.Find(ensaymadaId)!);
        state.OpenRecord(spanishId);

        state.List.SelectedTreeNodes.Should().Equal(state.List.SubAssemblyTree.Find(spanishId)!);
        state.List.SelectedTreeNode.Should().BeSameAs(state.List.SubAssemblyTree.Find(spanishId));
    });

    [Test]
    public Task The_folders_left_closed_stay_closed_next_time_and_a_new_role_opens() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var store = new InMemoryViewStateStore();
        (await LoadedStateAsync(store)).Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedStateAsync(store);

        first.List.SubAssemblyTree.Folder(doughRoleId)!.IsExpanded = false;

        var doughKey = doughRoleId.ToString(CultureInfo.InvariantCulture);
        await Settle.EventuallyAsync(
            () => store.ReadList(SubAssembliesList.ClosedFoldersKey)?.Contains(doughKey) == true,
            () => string.Join(", ", store.ReadList(SubAssembliesList.ClosedFoldersKey) ?? []));
        first.Dispose();

        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 3);
        var next = await LoadedStateAsync(store);
        next.List.SubAssemblyTree.Folder(doughRoleId)!.IsExpanded.Should().BeFalse();
        next.List.SubAssemblyTree.Folder(fillingRoleId)!.IsExpanded.Should().BeTrue();
        next.List.SubAssemblyTree.Folder(glazeRoleId)!.IsExpanded.Should().BeTrue("a role new since opens, as every folder first did");

        // One closed just before the app closes is written as it closes.
        next.List.SubAssemblyTree.Folder(fillingRoleId)!.IsExpanded = false;
        next.Dispose();
        store.ReadList(SubAssembliesList.ClosedFoldersKey).Should().Contain(fillingRoleId.ToString(CultureInfo.InvariantCulture));
    });

    private static IEnumerable<string> Headers(IEnumerable<TreeMenuItem> menu) => menu.Where(i => !i.IsSeparator).Select(i => i.Header);

    /// <summary>The sub-assembly's menu's Move To item for <paramref name="role"/>.</summary>
    private static TreeMenuItem MoveToItem(SubAssembliesState state, int subAssemblyId, string role) =>
        state.List.TreeMenuFor(state.List.SubAssemblyTree.Find(subAssemblyId)).Single(i => i.Header == "Move to").Items.Single(i => i.Header == role);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    [Test]
    public Task Picking_another_sub_assembly_over_unsaved_changes_asks_and_Cancel_stays_on_them() => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var plainId = await AddRecipeAsync("Plain Dough", doughId);
        var state = await OpenedStateAsync(sweetId);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        state.EditingName = "Sweeter Dough";

        Click(state, state.List.SubAssemblyTree.Find(plainId)!);
        await Settle.UntilAsync(state, () => asked.Count == 1, () => $"asked={asked.Count}");

        asked.Single().Should().StartWith("Save the changes to Sweeter Dough?");
        state.SelectedRow!.Id.Should().Be(sweetId);
        state.List.SelectedTreeNode.Should().BeSameAs(state.List.SubAssemblyTree.Find(sweetId), "the list goes back to the row showing");
        state.EditingName.Should().Be("Sweeter Dough");
        state.History.IsDirty.Should().BeTrue();
    });

    [Test]
    [Arguments(UnsavedChangesAnswer.Save, "Sweeter Dough")]
    [Arguments(UnsavedChangesAnswer.DontSave, "Sweet Dough")]
    public Task Picking_another_sub_assembly_over_unsaved_changes_opens_it_once_they_are_saved_or_not(UnsavedChangesAnswer answer, string savedName) => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var plainId = await AddRecipeAsync("Plain Dough", doughId);
        var state = await OpenedStateAsync(sweetId);
        AnswerUnsavedChanges(state, answer);
        state.EditingName = "Sweeter Dough";

        Click(state, state.List.SubAssemblyTree.Find(plainId)!);
        await Settle.UntilAsync(state,
            () => state.SelectedRow?.Id == plainId && state.EditingName == "Plain Dough" && !state.History.IsDirty,
            () => $"selected={state.SelectedRow?.Id} name={state.EditingName}");

        state.ErrorMessage.Should().BeNull();
        (await NameOfAsync(sweetId)).Should().Be(savedName);
    });

    [Test]
    public Task New_over_unsaved_changes_asks_and_Cancel_keeps_them() => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var state = await OpenedStateAsync(sweetId);
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.Cancel);
        state.EditingName = "Sweeter Dough";

        await state.NewCommand.ExecuteAsync();

        asked.Should().ContainSingle();
        state.SelectedRow!.Id.Should().Be(sweetId);
        state.EditingName.Should().Be("Sweeter Dough");
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved() => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var state = await OpenedStateAsync(sweetId);
        AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        state.EditingName = "Sweeter Dough";

        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();

        await Settle.UntilAsync(state, () => state.EditingName == "Sweet Dough" && !state.History.IsDirty, () => $"name={state.EditingName}");
        state.SelectedRow!.Id.Should().Be(sweetId);
        (await NameOfAsync(sweetId)).Should().Be("Sweet Dough");
    });

    // No puts a new sub-assembly back blank. What's typed into it next has to count, or it's lost with no question asked.
    [Test]
    public Task What_is_typed_after_no_on_a_new_sub_assembly_counts_as_unsaved() => UiThread.RunAsync(async () =>
    {
        var state = await LoadedStateAsync();
        var asked = AnswerUnsavedChanges(state, UnsavedChangesAnswer.DontSave);
        state.EditingName = "Sweet Dough";
        (await state.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();
        state.EditingName.Should().BeEmpty();
        state.History.IsDirty.Should().BeFalse();

        state.EditingName = "Sweeter Dough";

        state.History.IsDirty.Should().BeTrue();
        await state.ResolveUnsavedChangesAsync(revert: false);
        asked.Should().HaveCount(2);
    });

    /// <summary>Answers the question about unsaved changes with <paramref name="answer"/>, collecting each question asked.</summary>
    private static List<string> AnswerUnsavedChanges(SubAssembliesState state, UnsavedChangesAnswer answer)
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
    private static void Click(SubAssembliesState state, object row)
    {
        state.List.SelectedTreeNodes.Clear();
        state.List.SelectedTreeNodes.Add(row);
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
            case ScreenCommand<IReadOnlyList<SubAssemblyMove>> move:
                await move.ExecuteAsync((IReadOnlyList<SubAssemblyMove>)item.Parameter!);
                break;
            case ScreenCommand<SubAssemblyLeafNode> onSubAssembly:
                await onSubAssembly.ExecuteAsync((SubAssemblyLeafNode)item.Parameter!);
                break;
            case ScreenCommand<SubAssemblyFolderNode?> onFolder:
                // New Sub-Assembly, on the list's empty space, has no folder.
                await onFolder.ExecuteAsync((SubAssemblyFolderNode?)item.Parameter);
                break;
            case ScreenCommand plain:
                await plain.ExecuteAsync();
                break;
            default:
                throw new InvalidOperationException($"'{item.Header}' runs nothing a test knows how to wait for.");
        }
    }

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
    private static void Edit(SubAssembliesState state, Action change)
    {
        var recorder = (IUndoRecorder)state;
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

    private async Task<SubAssembliesState> LoadedStateAsync(IViewStateStore? viewStateStore = null)
    {
        var state = CreateState(viewStateStore);
        await state.LoadCommand.ExecuteAsync();
        return state;
    }

    /// <summary>The screen with <paramref name="subAssemblyId"/> open on the form, its lines in.</summary>
    private async Task<SubAssembliesState> OpenedStateAsync(int subAssemblyId)
    {
        var state = await LoadedStateAsync();
        state.OpenRecord(subAssemblyId);
        await Settle.UntilAsync(state,
            () => state.SelectedRow?.Id == subAssemblyId && state.Batch.Lines.Count > 0 && !state.History.IsDirty,
            () => $"lines={state.Batch.Lines.Count}");
        return state;
    }

    private SubAssembliesState CreateState(IViewStateStore? viewStateStore)
    {
        var factory = CreateDbContextFactory();

        return new SubAssembliesState(
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
}
