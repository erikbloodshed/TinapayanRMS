using System.Globalization;
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
/// The Sub-Assemblies list's right-click menus, its keys and its selection, as RawMaterialsViewModelTreeTests has them for
/// the Raw Materials list: what each menu offers a sub-assembly, several sub-assemblies, a role's folder and the list
/// itself, and what each item does, run as the menu runs it. A recipe only moves between recipe roles, and a packaging set
/// stays in the Packaging role.
/// </summary>
[NotInParallel(DatabaseCollection.B)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.B)]
public class SubAssembliesViewModelTreeTests(DatabaseFixture fixture) : PersistenceTestBase(fixture)
{
    [Test]
    public Task A_sub_assemblys_menu_moves_it_to_another_recipe_role_and_offers_rename_and_delete() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var doughId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        var leaf = viewModel.List.SubAssemblyTree.Find(doughId)!;
        viewModel.List.SelectedTreeNode = leaf;

        var menu = viewModel.List.TreeMenuFor(leaf);

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
        var viewModel = await LoadedViewModelAsync();
        var set = viewModel.List.SubAssemblyTree.Find(setId)!;

        Headers(viewModel.List.TreeMenuFor(set)).Should().Equal("Rename", "Delete");

        viewModel.List.SelectedTreeNode = viewModel.List.SubAssemblyTree.Find(doughId);
        viewModel.List.SelectedTreeNodes.Add(set);

        viewModel.List.TreeMenuFor(set).Should().BeEmpty();
    });

    [Test]
    public Task A_drop_moves_recipes_onto_another_roles_folder_and_gives_the_first_back_to_bring_into_view() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        IListTreeScreen screen = viewModel.List;
        var halaya = viewModel.List.SubAssemblyTree.Find(halayaId)!;
        var filling = viewModel.List.SubAssemblyTree.Folder(fillingRoleId)!;

        screen.CanDrag(halaya).Should().BeTrue();
        screen.CanDrag(filling).Should().BeFalse("a folder isn't dragged");
        screen.CanDrop([halaya], viewModel.List.SubAssemblyTree.Folder(doughRoleId)).Should().BeFalse("it's there already");

        (await screen.DropAsync([halaya], filling)).Should().BeSameAs(halaya);

        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        filling.SubAssemblies.Should().ContainSingle();
        viewModel.List.MoveNotice.Should().Be("Moved Ube Halaya to Filling");
    });

    [Test]
    public Task A_drop_made_while_a_move_is_still_saving_does_nothing() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var yemaId = await AddRecipeAsync("Yema", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        IListTreeScreen screen = viewModel.List;

        var firstMove = viewModel.List.MoveSubAssembliesCommand.Execute([new SubAssemblyMove(halayaId, fillingRoleId)]).ToTask();
        // As a drop would find it: the move under way (ReactiveCommand says so on its scheduler, a moment after Execute).
        await viewModel.List.MoveSubAssembliesCommand.CanExecute.FirstAsync(offered => !offered);
        (await screen.DropAsync([viewModel.List.SubAssemblyTree.Find(yemaId)!], viewModel.List.SubAssemblyTree.Folder(fillingRoleId)))
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
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(MoveToItem(viewModel, halayaId, "Filling"));

        viewModel.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        viewModel.List.SubAssemblyTree.Folder(fillingRoleId)!.SubAssemblies.Should().ContainSingle();
        viewModel.List.MoveNotice.Should().Be("Moved Ube Halaya to Filling");

        await viewModel.List.UndoMoveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(doughRoleId);
        viewModel.List.SubAssemblyTree.Folder(doughRoleId)!.SubAssemblies.Should().ContainSingle();
        viewModel.List.MoveNotice.Should().BeNull("an undone move leaves nothing to undo");
        (await viewModel.List.UndoMoveCommand.CanExecute.FirstAsync()).Should().BeFalse();
    });

    [Test]
    public Task Several_picked_move_together_and_the_one_showing_stays_on_the_form() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var jamId = await AddRecipeAsync("Ube Jam", doughRoleId);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var viewModel = await OpenedViewModelAsync(halayaId);
        var tree = viewModel.List.SubAssemblyTree;

        // Ctrl+clicks: the rows join the first, which stays the one showing.
        viewModel.List.SelectedTreeNodes.Add(tree.Find(jamId)!);
        viewModel.List.SelectedTreeNodes.Add(tree.Find(custardId)!);
        viewModel.SelectedRow!.Id.Should().Be(halayaId);

        var menu = viewModel.List.TreeMenuFor(tree.Find(jamId));

        Headers(menu).Should().Equal(["Move 3 Sub-Assemblies to"], "renaming or deleting is for one at a time");
        await RunAsync(menu[0].Items.Single(i => i.Header == "Filling"));

        viewModel.ErrorMessage.Should().BeNull();
        (await RoleOfAsync(halayaId)).Should().Be(fillingRoleId);
        (await RoleOfAsync(jamId)).Should().Be(fillingRoleId);
        viewModel.List.MoveNotice.Should().Be("Moved 2 sub-assemblies to Filling", "Custard was there already");
        viewModel.List.SelectedTreeSubAssemblies.Select(l => l.Row.Id).Should().Equal(halayaId, jamId, custardId);
        viewModel.SelectedRow!.Id.Should().Be(halayaId);
    });

    [Test]
    public Task A_move_of_the_one_showing_moves_its_Role_picker_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var viewModel = await OpenedViewModelAsync(halayaId);
        Edit(viewModel, () => viewModel.Batch.Lines[0].Grams = 250m);

        await RunAsync(MoveToItem(viewModel, halayaId, "Filling"));

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.EditingRole!.Id.Should().Be(fillingRoleId);
        viewModel.Batch.Lines[0].Grams.Should().Be(250m);
        viewModel.History.IsDirty.Should().BeTrue("the grams are still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.Batch.Lines[0].Grams.Should().Be(100m);
        viewModel.EditingRole!.Id.Should().Be(fillingRoleId, "no undo takes back a move that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
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
        var viewModel = await LoadedViewModelAsync();
        viewModel.List.SelectedTreeNode = viewModel.List.SubAssemblyTree.Find(glazeId);
        viewModel.List.SelectedTreeNodes.Add(viewModel.List.SubAssemblyTree.Find(ownGlazeId)!);

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(glazeId)).Single().Items.Single(i => i.Header == "Topping"));

        viewModel.ErrorMessage.Should().StartWith("Cream Loaf, which links this sub-assembly, has two Topping sub-assemblies called Glaze.");
        viewModel.List.MoveNotice.Should().BeNull();
        viewModel.List.SubAssemblyTree.Folder(toppingRoleId)!.SubAssemblies.Should().BeEmpty();
        (await RoleOfAsync(glazeId)).Should().Be(fillingRoleId);
    });

    [Test]
    public Task Rename_types_the_name_in_place_saves_it_and_leaves_what_else_is_typed_to_save() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        var viewModel = await OpenedViewModelAsync(halayaId);
        Edit(viewModel, () => viewModel.Batch.Lines[0].Grams = 250m);
        var halaya = viewModel.List.SubAssemblyTree.Find(halayaId)!;

        await RunAsync(viewModel.List.TreeMenuFor(halaya).Single(i => i.Header == "Rename"));

        viewModel.List.TreeNameEdit.Should().Be(new TreeNameEdit(halaya, "Ube Halaya"));

        await viewModel.List.CommitTreeNameAsync(" Ube Spread ");
        await viewModel.List.CommitTreeNameAsync("Ube Spread Again");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.TreeNameEdit.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var saved = await dbContext.SubAssemblies.SingleAsync(s => s.Id == halayaId, Cancellation);
            saved.Name.Should().Be("Ube Spread", "leaving the box after Enter saves nothing more");
            (await SubAssemblyTotalsAsync(halayaId)).TotalWeight.Should().Be(100m, "the rename saves the name alone");
        }

        viewModel.EditingName.Should().Be("Ube Spread");
        viewModel.HeaderTitle.Should().Be("Ube Spread");
        viewModel.List.SubAssemblyTree.Find(halayaId)!.Title.Should().Be("Ube Spread");
        viewModel.History.IsDirty.Should().BeTrue("the grams are still to be saved");

        viewModel.TryUndo().Should().BeTrue();

        viewModel.EditingName.Should().Be("Ube Spread", "no undo takes back a rename that's saved");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task A_rename_to_a_name_in_use_is_refused_says_why_and_brings_the_box_back() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var halayaId = await AddRecipeAsync("Ube Halaya", doughRoleId);
        await AddRecipeAsync("Ube Jam", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        var halaya = viewModel.List.SubAssemblyTree.Find(halayaId)!;

        await RunAsync(viewModel.List.TreeMenuFor(halaya).Single(i => i.Header == "Rename"));
        await viewModel.List.CommitTreeNameAsync("ube jam");

        viewModel.ErrorMessage.Should().NotBeNullOrEmpty();
        viewModel.List.SubAssemblyTree.Find(halayaId)!.Title.Should().Be("Ube Halaya");
        viewModel.List.TreeNameEdit.Should().Be(new TreeNameEdit(halaya, "ube jam"));

        // Esc, an empty name and the same name leave it be; so does a search.
        viewModel.List.CancelTreeName();
        viewModel.List.SelectedTreeNode = halaya;
        await RunAsync(viewModel.List.TreeShortcut("F2")!);
        await viewModel.List.CommitTreeNameAsync(" ");
        await RunAsync(viewModel.List.TreeShortcut("F2")!);
        await viewModel.List.CommitTreeNameAsync("Ube Halaya");
        await RunAsync(viewModel.List.TreeShortcut("F2")!);
        viewModel.List.SearchText = "ube";
        viewModel.List.TreeNameEdit.Should().BeNull();
        (await NameOfAsync(halayaId)).Should().Be("Ube Halaya");
    });

    [Test]
    public Task New_names_the_sub_assembly_in_its_roles_folder_and_the_row_stays_until_Save_files_it() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var ubeId = await AddIngredientAsync("Ube");
        var viewModel = await LoadedViewModelAsync();
        var filling = viewModel.List.SubAssemblyTree.Folder(fillingRoleId)!;
        viewModel.List.SearchText = "nothing like it";

        await RunAsync(viewModel.List.TreeMenuFor(filling).Single(i => i.Header == "New Sub-Assembly Here"));

        viewModel.List.SearchText.Should().BeEmpty("the search is cleared, so the new row shows");
        var row = viewModel.List.SubAssemblyTree.PendingSubAssembly!;
        filling.Children.Should().Equal(row);
        filling.SubAssemblyCount.Should().Be(0, "it isn't a sub-assembly until it's saved");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(row);
        viewModel.List.TreeNameEdit.Should().Be(new TreeNameEdit(row, "New sub-assembly"));
        viewModel.List.TreeMenuFor(row).Should().BeEmpty();
        viewModel.List.SelectedTreeSubAssemblies.Should().BeEmpty();
        viewModel.EditingRole!.Id.Should().Be(fillingRoleId);

        await viewModel.List.CommitTreeNameAsync("Ube Halaya");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.TreeNameEdit.Should().BeNull();
        row.Title.Should().Be("Ube Halaya");
        viewModel.EditingName.Should().Be("Ube Halaya");
        viewModel.History.IsDirty.Should().BeTrue("Save is offered for it");

        await viewModel.LoadCommand.Execute();
        filling.Children.Should().Equal([row], "a load keeps it");

        Edit(viewModel, () => viewModel.Batch.Lines.Add(new IngredientLineRow(viewModel.Ingredients.Single(i => i.Id == ubeId), 100m)));
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.SubAssemblyTree.PendingSubAssembly.Should().BeNull();
        filling.SubAssemblies.Select(l => l.Title).Should().Equal("Ube Halaya");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.SubAssemblyTree.Find(viewModel.SelectedRow!.Id));

        // In the Packaging role, a packaging set; Esc takes its row away, and Copy the form's.
        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Folder(packagingRoleId)).Single(i => i.Header == "New Packaging Set Here"));
        viewModel.List.TreeNameEdit!.InitialText.Should().Be("New packaging set");
        viewModel.List.CancelTreeName();
        viewModel.List.SubAssemblyTree.Folder(packagingRoleId)!.Children.Should().BeEmpty();
        viewModel.List.SelectedTreeNode.Should().BeNull();
    });

    [Test]
    public Task New_from_the_header_names_the_sub_assembly_in_the_first_role_and_Dont_Save_takes_the_row_away() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        await AddRoleAsync("Filling", sortOrder: 2);
        var viewModel = await LoadedViewModelAsync();
        AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        var dough = viewModel.List.SubAssemblyTree.Folder(doughRoleId)!;

        await viewModel.NewCommand.Execute();
        await viewModel.List.CommitTreeNameAsync("Sweet Dough");

        dough.Children.Select(c => c.Title).Should().Equal("Sweet Dough");

        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue("Don't Save puts the form back blank");

        viewModel.List.SubAssemblyTree.PendingSubAssembly.Should().BeNull("put back blank, it has no row");
        dough.Children.Should().BeEmpty();
        viewModel.List.Rows.Should().BeEmpty();
    });

    [Test]
    public Task New_Here_starts_a_sub_assembly_in_the_folders_role_that_Save_files_there() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 3, isPackaging: true);
        var ubeId = await AddIngredientAsync("Ube");
        var viewModel = await LoadedViewModelAsync();

        Headers(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Folder(packagingRoleId))).Should().StartWith(["New Packaging Set Here"]);

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Folder(fillingRoleId)).Single(i => i.Header == "New Sub-Assembly Here"));

        viewModel.SelectedRow.Should().BeNull();
        viewModel.EditingRole!.Id.Should().Be(fillingRoleId);
        viewModel.History.IsDirty.Should().BeFalse("nothing is typed yet");

        Edit(viewModel, () => viewModel.EditingName = "Ube Halaya");
        Edit(viewModel, () => viewModel.Batch.Lines.Add(new IngredientLineRow(viewModel.Ingredients.Single(i => i.Id == ubeId), 100m)));
        await viewModel.SaveCommand.Execute();

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.SubAssemblyTree.Folder(fillingRoleId)!.SubAssemblies.Select(l => l.Title).Should().Equal("Ube Halaya");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.SubAssemblyTree.Find(viewModel.SelectedRow!.Id));
    });

    [Test]
    public Task The_folder_and_list_menus_add_sub_assemblies_and_roles_and_open_or_close_every_folder() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var packagingRoleId = await AddRoleAsync("Packaging", sortOrder: 2, isPackaging: true);
        var viewModel = await LoadedViewModelAsync();
        var dough = viewModel.List.SubAssemblyTree.Folder(doughRoleId)!;

        Headers(viewModel.List.TreeMenuFor(dough))
            .Should().Equal("New Sub-Assembly Here", "New Folder", "Rename Role", "Delete Role", "Expand All", "Collapse All");
        Headers(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Folder(packagingRoleId)))
            .Should().Equal(["New Packaging Set Here", "New Folder", "Rename Role", "Expand All", "Collapse All"], "the Packaging role is never deleted");
        var listMenu = viewModel.List.TreeMenuFor(null);
        Headers(listMenu).Should().Equal("New Sub-Assembly", "New Folder", "Expand All", "Collapse All");

        await RunAsync(listMenu.Single(i => i.Header == "Collapse All"));
        viewModel.List.SubAssemblyTree.Roots.Should().OnlyContain(f => !f.IsExpanded);

        await RunAsync(viewModel.List.TreeMenuFor(dough).Single(i => i.Header == "Expand All"));
        viewModel.List.SubAssemblyTree.Roots.Should().OnlyContain(f => f.IsExpanded);
    });

    [Test]
    public Task New_Folder_names_a_role_in_place_after_the_others_and_picks_it() => UiThread.RunAsync(async () =>
    {
        await AddRoleAsync("Dough", sortOrder: 1);
        var viewModel = await LoadedViewModelAsync();

        await RunAsync(viewModel.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));

        var folder = viewModel.List.SubAssemblyTree.PendingFolder!;
        viewModel.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough", "New folder");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(folder);
        viewModel.List.TreeNameEdit!.Node.Should().BeSameAs(folder);
        viewModel.List.TreeNameEdit.Hint.Should().Contain("a section in the recipes that use it");
        viewModel.List.TreeMenuFor(folder).Should().BeEmpty();

        await viewModel.LoadCommand.Execute();
        viewModel.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal(["Dough", "New folder"], "a load keeps it");

        await viewModel.List.CommitTreeNameAsync("Glaze");

        viewModel.ErrorMessage.Should().BeNull();
        viewModel.List.SubAssemblyTree.PendingFolder.Should().BeNull();
        viewModel.Roles.Select(r => r.Name).Should().Equal("Dough", "Glaze");
        viewModel.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough", "Glaze");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.SubAssemblyTree.Roots[1]);

        await RunAsync(viewModel.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        await viewModel.List.CommitTreeNameAsync("dough");

        viewModel.Roles.Should().HaveCount(2, "a name that's a role already adds none");
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.SubAssemblyTree.Roots[0]);

        // Esc and an empty name add nothing.
        await RunAsync(viewModel.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        viewModel.List.CancelTreeName();
        await RunAsync(viewModel.List.TreeMenuFor(null).Single(i => i.Header == "New Folder"));
        await viewModel.List.CommitTreeNameAsync(string.Empty);
        viewModel.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough", "Glaze");
        viewModel.Roles.Should().HaveCount(2);
    });

    [Test]
    public Task Rename_Role_types_the_name_in_place_keeps_it_product_owned_and_the_picker_follows() => UiThread.RunAsync(async () =>
    {
        var toppingRoleId = await AddRoleAsync("Topping", sortOrder: 1, isProductOwned: true);
        var glazeId = await AddRecipeAsync("Glaze", toppingRoleId);
        var viewModel = await OpenedViewModelAsync(glazeId);
        var topping = viewModel.List.SubAssemblyTree.Folder(toppingRoleId)!;

        await RunAsync(viewModel.List.TreeMenuFor(topping).Single(i => i.Header == "Rename Role"));

        viewModel.List.TreeNameEdit!.Node.Should().BeSameAs(topping);
        viewModel.List.TreeNameEdit.InitialText.Should().Be("Topping");
        await viewModel.List.CommitTreeNameAsync("Toppings");

        viewModel.ErrorMessage.Should().BeNull();
        await using (var dbContext = CreateDbContext())
        {
            var role = await dbContext.SubAssemblyRoles.SingleAsync(Cancellation);
            (role.Name, role.IsProductOwned).Should().Be(("Toppings", true));
        }

        viewModel.List.SubAssemblyTree.Folder(toppingRoleId)!.Title.Should().Be("Toppings");
        viewModel.EditingRole!.Name.Should().Be("Toppings");
        viewModel.History.IsDirty.Should().BeFalse();
    });

    [Test]
    public Task Delete_Role_is_greyed_while_a_sub_assembly_is_in_it_and_deletes_an_empty_one_once_asked() => UiThread.RunAsync(async () =>
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
        viewModel.List.SearchText = "Pandesal";
        var doughMenu = viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Folder(doughRoleId));
        doughMenu.Single(i => i.Header.StartsWith("Delete Role", StringComparison.Ordinal)).Should()
            .Match<TreeMenuItem>(i => i.Header == "Delete Role (holds 2)" && !i.IsEnabled);
        viewModel.List.SelectedTreeNode = viewModel.List.SubAssemblyTree.Folder(doughRoleId);
        viewModel.List.TreeShortcut("Del").Should().BeNull("Del does nothing where the menu's item is greyed");
        viewModel.List.SearchText = string.Empty;

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Folder(glazeRoleId)).Single(i => i.Header == "Delete Role"));

        viewModel.ErrorMessage.Should().BeNull();
        question.Should().Be("Delete the 'Glaze' role? It holds no sub-assemblies here. This can't be undone.");
        viewModel.Roles.Select(r => r.Name).Should().Equal("Dough");
        viewModel.List.SubAssemblyTree.Roots.Select(f => f.Title).Should().Equal("Dough");
    });

    [Test]
    public Task Delete_on_a_sub_assembly_asks_first_and_the_form_starts_afresh_only_if_it_was_showing_it() => UiThread.RunAsync(async () =>
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

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(ensaymadaId)).Single(i => i.Header == "Delete"));

        question.Should().Be("Delete 'Ensaymada Dough'? This can't be undone.");
        viewModel.List.Rows.Select(r => r.Name).Should().Equal("Pandesal Dough");
        viewModel.SelectedRow!.Id.Should().Be(pandesalId, "the one showing wasn't the one deleted");

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(pandesalId)).Single(i => i.Header == "Delete"));

        viewModel.List.Rows.Should().BeEmpty();
        viewModel.SelectedRow.Should().BeNull();
        viewModel.EditingName.Should().BeEmpty();
    });

    [Test]
    public Task Make_In_House_Ingredient_makes_one_from_the_recipe_which_stays_on_the_list_and_lists_it_under_Used_By() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var inHouseId = await MadeCategoryIdAsync();
        var viewModel = await OpenedViewModelAsync(custardId);
        ChoicePromptRequest? asked = null;
        viewModel.List.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked = interaction.Input;
            interaction.SetOutput(new ChoicePromptResult("Custard Filling", interaction.Input.InitialChoice));
        });

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(custardId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().BeNull();
        asked!.Title.Should().Be("Make In-House Ingredient");
        asked.InitialValue.Should().Be("Custard", "the name starts as the sub-assembly's");
        asked.Choices[asked.InitialChoice].Should().Be("In-House");
        asked.Message.Should().Contain("Custard stays here as it is");
        viewModel.List.Rows.Should().ContainSingle(r => r.Id == custardId, "the recipe stays where it's written");
        viewModel.SuccessMessage.Should().Be("Custard Filling was added to In-House, made from Custard.");
        var made = viewModel.Ingredients.Should().ContainSingle(m => m.Name == "Custard Filling").Subject;
        made.Should().BeEquivalentTo(new { Source = RawMaterialSource.Made, CategoryId = inHouseId, WeightLossPercent = 0m, SubAssemblyId = (int?)custardId });
        viewModel.Dependents.MadeIngredients.Should().Equal(new MadeIngredientUse(made.Id, "Custard Filling"));

        var opened = new List<int>();
        using var subscription = viewModel.Dependents.RawMaterialOpenRequests.Subscribe(opened.Add);
        await viewModel.Dependents.OpenRawMaterialCommand.Execute(made.Id);
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
        var viewModel = await OpenedViewModelAsync(baseId);
        string? message = null;
        viewModel.List.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            message = interaction.Input.Message;
            interaction.SetOutput(new ChoicePromptResult("Ube Filling", 0));
        });

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(baseId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().BeNull();
        message.Should().Contain("Ube Base stays here as it is");
        viewModel.SuccessMessage.Should().Be("Ube Filling was added to In-House, made from Ube Base.");
        viewModel.SelectedRow!.Id.Should().Be(baseId, "it stays on the list, and on the form");
        viewModel.Batch.Lines.Select(l => (l.Ingredient!.Name, l.Grams)).Should().Equal(("Ube", 300m), ("Milk", 100m));
        viewModel.Batch.BatchCost.Should().Be(70m, "₱60 of ube and ₱10 of milk, as before");
        viewModel.Dependents.Products.Select(p => p.Name).Should().Equal("Ube Bun");
        viewModel.Dependents.MadeIngredients.Select(m => m.Name).Should().Equal("Ube Filling");
        viewModel.History.IsDirty.Should().BeFalse("the form shows it as it's saved");
        await using var dbContext = CreateDbContext();
        (await dbContext.ProductVersions.AnyAsync(v => v.ProductId == productId, Cancellation)).Should().BeFalse("nothing about Ube Bun changed");
    });

    [Test]
    public Task Make_In_House_Ingredient_waits_for_the_forms_changes_and_needs_a_Made_category() => UiThread.RunAsync(async () =>
    {
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 1);
        var custardId = await AddRecipeAsync("Custard", fillingRoleId);
        var flanId = await AddRecipeAsync("Leche Flan", fillingRoleId);
        var viewModel = await OpenedViewModelAsync(custardId);
        var asked = 0;
        viewModel.List.PromptForMadeIngredient.RegisterHandler(interaction =>
        {
            asked++;
            interaction.SetOutput(null);
        });
        Edit(viewModel, () => viewModel.EditingName = "Custard Base");

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(custardId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().Be("Save Custard first, or undo its changes: the made ingredient is made from it as it's saved.");

        await RunAsync(viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(flanId)).Single(i => i.Header == "Make In-House Ingredient…"));

        viewModel.ErrorMessage.Should().Be(MadeIngredientPrompt.NoMadeCategoryMessage);
        asked.Should().Be(0);
        viewModel.List.Rows.Should().HaveCount(2);
    });


    [Test]
    public Task F2_and_Del_do_what_the_menu_shows_for_the_one_row_picked() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 2);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var viewModel = await LoadedViewModelAsync();
        var pandesal = viewModel.List.SubAssemblyTree.Find(pandesalId)!;

        viewModel.List.SelectedTreeNode = pandesal;
        viewModel.List.TreeShortcut("F2").Should().Match<TreeMenuItem>(i => i.Command == viewModel.List.RenameSubAssemblyCommand && i.Parameter == pandesal);
        viewModel.List.TreeShortcut("Del").Should().Match<TreeMenuItem>(i => i.Command == viewModel.List.DeleteSubAssemblyCommand && i.Parameter == pandesal);

        viewModel.List.SelectedTreeNode = viewModel.List.SubAssemblyTree.Folder(glazeRoleId);
        viewModel.List.TreeShortcut("F2")!.Command.Should().BeSameAs(viewModel.List.RenameRoleCommand);
        viewModel.List.TreeShortcut("Del")!.Command.Should().BeSameAs(viewModel.List.DeleteRoleCommand);

        viewModel.List.SelectedTreeNode = pandesal;
        viewModel.List.SelectedTreeNodes.Add(viewModel.List.SubAssemblyTree.Find(ensaymadaId)!);
        viewModel.List.TreeShortcut("Del").Should().BeNull("with several picked, a key could take the wrong one");
    });

    [Test]
    public Task Picking_a_row_picks_its_sub_assembly_and_one_opened_from_elsewhere_is_picked_alone() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var pandesalId = await AddRecipeAsync("Pandesal Dough", doughRoleId);
        var ensaymadaId = await AddRecipeAsync("Ensaymada Dough", doughRoleId);
        var spanishId = await AddRecipeAsync("Spanish Bread Dough", doughRoleId);
        var viewModel = await LoadedViewModelAsync();

        viewModel.List.SelectedTreeNode = viewModel.List.SubAssemblyTree.Find(pandesalId);
        viewModel.SelectedRow!.Id.Should().Be(pandesalId);

        viewModel.List.SelectedTreeNode = viewModel.List.SubAssemblyTree.Folder(doughRoleId);
        viewModel.SelectedRow!.Id.Should().Be(pandesalId, "a folder picked leaves the sub-assembly showing");

        viewModel.List.SelectedTreeNode = viewModel.List.SubAssemblyTree.Find(pandesalId);
        viewModel.List.SelectedTreeNodes.Add(viewModel.List.SubAssemblyTree.Find(ensaymadaId)!);
        viewModel.OpenRecord(spanishId);

        viewModel.List.SelectedTreeNodes.Should().Equal(viewModel.List.SubAssemblyTree.Find(spanishId)!);
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.SubAssemblyTree.Find(spanishId));
    });

    [Test]
    public Task The_folders_left_closed_stay_closed_next_time_and_a_new_role_opens() => UiThread.RunAsync(async () =>
    {
        var doughRoleId = await AddRoleAsync("Dough", sortOrder: 1);
        var fillingRoleId = await AddRoleAsync("Filling", sortOrder: 2);
        var store = new InMemoryViewStateStore();
        (await LoadedViewModelAsync(store)).Dispose();
        store.WriteCount.Should().Be(0, "nothing opened or closed is nothing to remember");
        var first = await LoadedViewModelAsync(store);

        first.List.SubAssemblyTree.Folder(doughRoleId)!.IsExpanded = false;

        var doughKey = doughRoleId.ToString(CultureInfo.InvariantCulture);
        await Settle.EventuallyAsync(
            () => store.ReadList(SubAssembliesList.ClosedFoldersKey)?.Contains(doughKey) == true,
            () => string.Join(", ", store.ReadList(SubAssembliesList.ClosedFoldersKey) ?? []));
        first.Dispose();

        var glazeRoleId = await AddRoleAsync("Glaze", sortOrder: 3);
        var next = await LoadedViewModelAsync(store);
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
    private static TreeMenuItem MoveToItem(SubAssembliesViewModel viewModel, int subAssemblyId, string role) =>
        viewModel.List.TreeMenuFor(viewModel.List.SubAssemblyTree.Find(subAssemblyId)).Single(i => i.Header == "Move to").Items.Single(i => i.Header == role);

    /// <summary>Runs the item's command with its parameter, as the menu does when it's clicked, and waits for it to finish.</summary>
    [Test]
    public Task Picking_another_sub_assembly_over_unsaved_changes_asks_and_Cancel_stays_on_them() => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var plainId = await AddRecipeAsync("Plain Dough", doughId);
        var viewModel = await OpenedViewModelAsync(sweetId);
        var asked = AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.Cancel);
        viewModel.EditingName = "Sweeter Dough";

        Click(viewModel, viewModel.List.SubAssemblyTree.Find(plainId)!);
        await Settle.UntilAsync(viewModel, () => asked.Count == 1, () => $"asked={asked.Count}");

        asked.Single().Should().StartWith("Save the changes to Sweeter Dough?");
        viewModel.SelectedRow!.Id.Should().Be(sweetId);
        viewModel.List.SelectedTreeNode.Should().BeSameAs(viewModel.List.SubAssemblyTree.Find(sweetId), "the list goes back to the row showing");
        viewModel.EditingName.Should().Be("Sweeter Dough");
        viewModel.History.IsDirty.Should().BeTrue();
    });

    [Test]
    [Arguments(UnsavedChangesAnswer.Save, "Sweeter Dough")]
    [Arguments(UnsavedChangesAnswer.DontSave, "Sweet Dough")]
    public Task Picking_another_sub_assembly_over_unsaved_changes_opens_it_once_they_are_saved_or_not(UnsavedChangesAnswer answer, string savedName) => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var plainId = await AddRecipeAsync("Plain Dough", doughId);
        var viewModel = await OpenedViewModelAsync(sweetId);
        AnswerUnsavedChanges(viewModel, answer);
        viewModel.EditingName = "Sweeter Dough";

        Click(viewModel, viewModel.List.SubAssemblyTree.Find(plainId)!);
        await Settle.UntilAsync(viewModel,
            () => viewModel.SelectedRow?.Id == plainId && viewModel.EditingName == "Plain Dough" && !viewModel.History.IsDirty,
            () => $"selected={viewModel.SelectedRow?.Id} name={viewModel.EditingName}");

        viewModel.ErrorMessage.Should().BeNull();
        (await NameOfAsync(sweetId)).Should().Be(savedName);
    });

    [Test]
    public Task New_over_unsaved_changes_asks_and_Cancel_keeps_them() => UiThread.RunAsync(async () =>
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
    });

    [Test]
    public Task Leaving_the_screen_without_saving_puts_the_form_back_as_it_was_saved() => UiThread.RunAsync(async () =>
    {
        var doughId = await AddRoleAsync("Dough");
        var sweetId = await AddRecipeAsync("Sweet Dough", doughId);
        var viewModel = await OpenedViewModelAsync(sweetId);
        AnswerUnsavedChanges(viewModel, UnsavedChangesAnswer.DontSave);
        viewModel.EditingName = "Sweeter Dough";

        (await viewModel.ResolveUnsavedChangesAsync(revert: true)).Should().BeTrue();

        await Settle.UntilAsync(viewModel, () => viewModel.EditingName == "Sweet Dough" && !viewModel.History.IsDirty, () => $"name={viewModel.EditingName}");
        viewModel.SelectedRow!.Id.Should().Be(sweetId);
        (await NameOfAsync(sweetId)).Should().Be("Sweet Dough");
    });

    // No puts a new sub-assembly back blank. What's typed into it next has to count, or it's lost with no question asked.
    [Test]
    public Task What_is_typed_after_no_on_a_new_sub_assembly_counts_as_unsaved() => UiThread.RunAsync(async () =>
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
    });

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
        viewModel.List.SelectedTreeNodes.Clear();
        viewModel.List.SelectedTreeNodes.Add(row);
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

    /// <summary>One finished change, as the fields and line tables bracket a gesture in the app.</summary>
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
        viewModel.OpenRecord(subAssemblyId);
        await Settle.UntilAsync(viewModel,
            () => viewModel.SelectedRow?.Id == subAssemblyId && viewModel.Batch.Lines.Count > 0 && !viewModel.History.IsDirty,
            () => $"lines={viewModel.Batch.Lines.Count}");
        return viewModel;
    }

    private SubAssembliesViewModel CreateViewModel(IViewStateStore? viewStateStore)
    {
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
}
