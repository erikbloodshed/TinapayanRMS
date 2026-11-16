using System.Globalization;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.SubAssemblies;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Tests.SubAssemblies;

/// <summary>The Sub-Assemblies list's tree: a folder per role, in the roles' order, holding the sub-assemblies in it.</summary>
public class SubAssemblyTreeTests
{
    private static readonly SubAssemblyRole Dough = new() { Id = 1, Name = "Dough", SortOrder = 2 };
    private static readonly SubAssemblyRole Sponge = new() { Id = 2, Name = "Sponge", SortOrder = 1 };
    private static readonly SubAssemblyRole Filling = new() { Id = 3, Name = "Filling", SortOrder = 3 };
    private static readonly SubAssemblyRole Packaging = new() { Id = 4, Name = "Packaging", SortOrder = 4, IsPackaging = true };

    private static readonly SubAssemblyRole[] Roles = [Sponge, Dough, Filling, Packaging];

    private static StandaloneSubAssemblyRow RowOf(int id, string name, SubAssemblyRole role) =>
        new(new SubAssembly { Id = id, Name = name, RoleId = role.Id, Role = role }, []);

    /// <summary>What the tree remembers the role's folder by.</summary>
    private static string Key(SubAssemblyRole role) => role.Id.ToString(CultureInfo.InvariantCulture);

    private static IEnumerable<string> TitlesIn(SubAssemblyFolderNode folder) => folder.Children.Select(c => c.Title);

    [Test]
    public void Each_role_has_a_folder_in_the_roles_order_holding_its_sub_assemblies_in_the_lists_order()
    {
        var tree = new SubAssemblyTree();

        // In the order the list shows them, which is by name (SubAssembliesState.VisibleRows).
        tree.Refresh(
            Roles,
            [RowOf(2, "Ensaymada Dough", Dough), RowOf(1, "Pandesal Dough", Dough), RowOf(3, "Bread Bag Set", Packaging)],
            isFiltered: false);

        tree.Roots.Select(r => r.Title).Should().Equal("Sponge", "Dough", "Filling", "Packaging");
        TitlesIn(tree.Folder(Dough.Id)!).Should().Equal("Ensaymada Dough", "Pandesal Dough");
        TitlesIn(tree.Folder(Sponge.Id)!).Should().BeEmpty("an empty folder still shows, to be dropped on");
        tree.Folder(Dough.Id)!.SubAssemblyCount.Should().Be(2);
        tree.Folder(Packaging.Id)!.IsPackaging.Should().BeTrue();
        tree.Roots.Should().OnlyContain(r => r.IsExpanded, "the list first shows every sub-assembly");
    }

    [Test]
    public void A_search_shows_only_the_folders_holding_a_match_and_opens_them()
    {
        var tree = new SubAssemblyTree(new FolderMemory([Key(Dough), Key(Filling)]));
        StandaloneSubAssemblyRow[] rows = [RowOf(1, "Pandesal Dough", Dough)];
        tree.Refresh(Roles, rows, isFiltered: false);
        tree.Folder(Dough.Id)!.IsExpanded.Should().BeFalse("it was left closed");

        tree.Refresh(Roles, rows, isFiltered: true);

        tree.Roots.Should().Equal(tree.Folder(Dough.Id)!);
        tree.Folder(Dough.Id)!.IsExpanded.Should().BeTrue("every match shows");

        tree.Refresh(Roles, rows, isFiltered: false);
        tree.Roots.Should().HaveCount(4);
        tree.Folder(Dough.Id)!.IsExpanded.Should().BeTrue("a folder stays as it was left");
        tree.Folder(Filling.Id)!.IsExpanded.Should().BeFalse("the search didn't show it");
    }

    [Test]
    public void A_sub_assembly_keeps_its_row_through_a_refresh_and_takes_it_to_its_new_role()
    {
        var tree = new SubAssemblyTree();
        tree.Refresh(Roles, [RowOf(1, "Ube Halaya", Dough)], isFiltered: false);
        var row = tree.Find(1)!;

        tree.Refresh(Roles, [RowOf(1, "Ube Halaya", Filling)], isFiltered: false);

        tree.Find(1).Should().BeSameAs(row);
        tree.Folder(Filling.Id)!.SubAssemblies.Should().Equal(row);
        tree.Folder(Dough.Id)!.SubAssemblies.Should().BeEmpty();
    }

    [Test]
    public void A_renamed_role_keeps_its_folder_a_reordered_one_moves_and_a_deleted_ones_goes()
    {
        var tree = new SubAssemblyTree();
        tree.Refresh(Roles, [], isFiltered: false);
        var dough = tree.Folder(Dough.Id);

        tree.Refresh(
            [new SubAssemblyRole { Id = Dough.Id, Name = "Doughs", SortOrder = 1 }, new SubAssemblyRole { Id = Sponge.Id, Name = "Sponge", SortOrder = 2 }, Packaging],
            [],
            isFiltered: false);

        tree.Roots.Select(r => r.Title).Should().Equal("Doughs", "Sponge", "Packaging");
        tree.Folder(Dough.Id).Should().BeSameAs(dough);
        tree.Folder(Filling.Id).Should().BeNull();
    }

    [Test]
    public void A_drop_moves_a_recipe_only_into_another_recipe_role_and_a_set_nowhere()
    {
        var tree = new SubAssemblyTree();
        var pandesal = RowOf(1, "Pandesal Dough", Dough);
        var ube = RowOf(2, "Ube Halaya", Filling);
        var set = RowOf(3, "Bread Bag Set", Packaging);
        tree.Refresh(Roles, [pandesal, ube, set], isFiltered: false);

        SubAssemblyTree.MoveTo(tree.Folder(Sponge.Id), pandesal.SubAssembly).Should().Be(new SubAssemblyMove(1, Sponge.Id));
        SubAssemblyTree.MoveTo(tree.Find(2), pandesal.SubAssembly).Should().Be(new SubAssemblyMove(1, Filling.Id), "onto a sub-assembly is into its folder");

        SubAssemblyTree.MoveTo(tree.Folder(Dough.Id), pandesal.SubAssembly).Should().BeNull("it's there already");
        SubAssemblyTree.MoveTo(tree.Folder(Packaging.Id), pandesal.SubAssembly).Should().BeNull("a recipe isn't a packaging set");
        SubAssemblyTree.MoveTo(tree.Find(3), pandesal.SubAssembly).Should().BeNull();
        SubAssemblyTree.MoveTo(tree.Folder(Dough.Id), set.SubAssembly).Should().BeNull("a packaging set stays in the Packaging role");
        SubAssemblyTree.MoveTo(null, pandesal.SubAssembly).Should().BeNull();
    }

    [Test]
    public void Several_sub_assemblies_move_together_leaving_out_those_that_would_not_move()
    {
        SubAssembly[] subAssemblies =
        [
            RowOf(1, "Pandesal Dough", Dough).SubAssembly,
            RowOf(2, "Ube Halaya", Filling).SubAssembly,
            RowOf(3, "Bread Bag Set", Packaging).SubAssembly,
        ];

        SubAssemblyTree.MovesTo(Filling, subAssemblies).Should().Equal(new SubAssemblyMove(1, Filling.Id));
        SubAssemblyTree.MovesTo(Packaging, subAssemblies).Should().BeEmpty();
    }

    [Test]
    public void A_folder_with_its_first_rows_is_announced_open_again()
    {
        var tree = new SubAssemblyTree();
        var announced = 0;

        // The roles come in first, as a load merges them, so the folder is made open but empty.
        tree.Refresh(Roles, [], isFiltered: false);
        var dough = tree.Folder(Dough.Id)!;
        dough.PropertyChanged += (_, e) => announced += e.PropertyName == nameof(SubAssemblyFolderNode.IsExpanded) ? 1 : 0;

        tree.Refresh(Roles, [RowOf(1, "Pandesal Dough", Dough)], isFiltered: false);

        announced.Should().Be(1, "the tree can't open a folder with nothing in it, and has to be told again once it has rows");

        tree.Refresh(Roles, [RowOf(1, "Pandesal Dough", Dough), RowOf(2, "Ensaymada Dough", Dough)], isFiltered: false);

        announced.Should().Be(1, "it had rows already");
    }

    [Test]
    public void The_folders_closed_are_what_is_remembered_and_a_tree_never_refreshed_keeps_what_it_was_given()
    {
        var tree = new SubAssemblyTree(new FolderMemory([Key(Filling), "99"]));
        tree.ClosedFolderKeys().Should().Equal(Key(Filling), "99");

        tree.Refresh(Roles, [], isFiltered: false);

        tree.ClosedFolderKeys().Should().Equal([Key(Filling)], "a role gone since has no folder to keep closed");
        tree.Folder(Dough.Id)!.IsExpanded.Should().BeTrue("a folder not left closed opens");
    }

    [Test]
    public void Expand_All_and_Collapse_All_open_or_close_every_folder()
    {
        var tree = new SubAssemblyTree();
        tree.Refresh(Roles, [], isFiltered: false);

        tree.CollapseAll();
        tree.Roots.Should().OnlyContain(r => !r.IsExpanded);
        tree.ClosedFolderKeys().Should().HaveCount(4);

        tree.ExpandAll();
        tree.Roots.Should().OnlyContain(r => r.IsExpanded);
        tree.ClosedFolderKeys().Should().BeEmpty();
    }

    [Test]
    public void A_new_folder_sits_after_the_roles_until_it_ends_and_a_refresh_keeps_it()
    {
        var tree = new SubAssemblyTree();
        tree.Refresh(Roles, [], isFiltered: false);

        var folder = tree.BeginNewFolder();

        tree.PendingFolder.Should().BeSameAs(folder);
        folder.Should().Match<SubAssemblyFolderNode>(f => f.IsNewFolder && f.RoleId == 0 && !f.IsPackaging && f.Title == "New folder");
        folder.Key.Should().Be("New");
        tree.Roots.Select(r => r.Title).Should().Equal("Sponge", "Dough", "Filling", "Packaging", "New folder");

        tree.Refresh(Roles, [], isFiltered: false);
        tree.Roots.Select(r => r.Title).Should().Equal("Sponge", "Dough", "Filling", "Packaging", "New folder");
        SubAssemblyTree.MoveTo(folder, RowOf(1, "Pandesal Dough", Dough).SubAssembly).Should().BeNull("nothing is dropped on a folder that isn't a role yet");

        tree.EndNewFolder();
        tree.PendingFolder.Should().BeNull();
        tree.Roots.Select(r => r.Title).Should().Equal("Sponge", "Dough", "Filling", "Packaging");
        tree.ClosedFolderKeys().Should().NotContain("New");
    }

    [Test]
    public void A_new_sub_assembly_sits_last_in_its_roles_folder_uncounted_until_it_ends_and_a_refresh_keeps_it()
    {
        var tree = new SubAssemblyTree();
        var pandesal = RowOf(1, "Pandesal Dough", Dough);
        tree.Refresh(Roles, [pandesal], isFiltered: false);
        var dough = tree.Folder(Dough.Id)!;
        dough.IsExpanded = false;

        var row = tree.BeginNewSubAssembly(new SubAssembly { Name = "New sub-assembly", RoleId = Dough.Id, Role = Dough })!;

        tree.PendingSubAssembly.Should().BeSameAs(row);
        row.IsNew.Should().BeTrue();
        TitlesIn(dough).Should().Equal("Pandesal Dough", "New sub-assembly");
        dough.IsExpanded.Should().BeTrue("its folder opens to show it");

        tree.Refresh(Roles, [pandesal], isFiltered: false);
        TitlesIn(dough).Should().Equal("Pandesal Dough", "New sub-assembly");
        dough.SubAssemblyCount.Should().Be(1, "it isn't a sub-assembly until it's saved");
        tree.Find(0).Should().BeNull();
        SubAssemblyTree.MoveTo(row, RowOf(2, "Ube Halaya", Filling).SubAssembly).Should().BeNull("nothing is dropped on it");
        SubAssemblyTree.MoveTo(tree.Folder(Filling.Id), row.SubAssembly).Should().BeNull("and it isn't moved");

        tree.EndNewSubAssembly();
        tree.PendingSubAssembly.Should().BeNull();
        TitlesIn(dough).Should().Equal("Pandesal Dough");
        tree.BeginNewSubAssembly(new SubAssembly { Name = "New sub-assembly", RoleId = 99 }).Should().BeNull("a role with no folder has nowhere to put it");
    }
}
