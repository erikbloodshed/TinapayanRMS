using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>The Raw Materials list's tree: a folder per type, holding a folder per category of that type, holding materials.</summary>
public class RawMaterialTreeTests
{
    private static readonly RawMaterialCategory Flour = new() { Id = 1, Name = "Flour", Type = RawMaterialCategoryType.Ingredient };
    private static readonly RawMaterialCategory Dairy = new() { Id = 2, Name = "Dairy", Type = RawMaterialCategoryType.Ingredient };
    private static readonly RawMaterialCategory Boxes = new() { Id = 3, Name = "Boxes", Type = RawMaterialCategoryType.Packaging };

    private static readonly RawMaterialCategory[] Categories = [Flour, Dairy, Boxes];

    private static RawMaterial MaterialOf(int id, string name, RawMaterialCategory category) =>
        new() { Id = id, Name = name, Type = RawMaterialRules.MaterialTypeOf(category.Type), CategoryId = category.Id, Category = category };

    private static RawMaterialFolderNode Folder(RawMaterialTree tree, RawMaterialType type, string title) =>
        tree.TypeFolder(type).Children.OfType<RawMaterialFolderNode>().Single(f => f.Title == title);

    private static IEnumerable<string> TitlesIn(RawMaterialFolderNode folder) => folder.Children.Select(c => c.Title);

    [Fact]
    public void The_types_are_the_top_level_folders_Ingredients_first()
    {
        var tree = new RawMaterialTree();

        tree.Roots.Select(r => r.Title).Should().Equal("Ingredients", "Packaging");
        tree.Roots.Should().OnlyContain(r => r.IsTypeFolder && r.CategoryId == null && r.IsExpanded);
    }

    [Fact]
    public void Each_type_holds_its_own_categories_by_name_and_each_folder_its_materials_in_the_lists_order()
    {
        var tree = new RawMaterialTree();

        // In the order the list shows them, which is by name (RawMaterialsViewModel.VisibleRawMaterials).
        tree.Refresh(
            Categories,
            [
                MaterialOf(2, "All-Purpose Flour", Flour),
                MaterialOf(1, "Bread Flour", Flour),
                MaterialOf(3, "Cake Box", Boxes),
            ],
            isFiltered: false);

        TitlesIn(tree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Dairy", "Flour");
        TitlesIn(tree.TypeFolder(RawMaterialType.Packaging)).Should().Equal("Boxes");
        TitlesIn(Folder(tree, RawMaterialType.Ingredient, "Flour")).Should().Equal("All-Purpose Flour", "Bread Flour");
        TitlesIn(Folder(tree, RawMaterialType.Ingredient, "Dairy")).Should().BeEmpty("an empty folder still shows, to be dropped on");
        Folder(tree, RawMaterialType.Ingredient, "Flour").MaterialCount.Should().Be(2);
        tree.TypeFolder(RawMaterialType.Ingredient).MaterialCount.Should().Be(2);
        tree.TypeFolder(RawMaterialType.Packaging).MaterialCount.Should().Be(1);
    }

    [Fact]
    public void A_search_shows_only_the_folders_holding_a_match_and_opens_them()
    {
        var tree = new RawMaterialTree();
        RawMaterial[] materials = [MaterialOf(1, "Bread Flour", Flour)];
        tree.Refresh(Categories, materials, isFiltered: false);
        Folder(tree, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse("a category folder starts closed");

        tree.Refresh(Categories, materials, isFiltered: true);

        TitlesIn(tree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour");
        TitlesIn(tree.TypeFolder(RawMaterialType.Packaging)).Should().BeEmpty("the types always show, even with no match");
        Folder(tree, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue("every match shows");

        tree.Refresh(Categories, materials, isFiltered: false);
        Folder(tree, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue("a folder stays as it was left");
        Folder(tree, RawMaterialType.Ingredient, "Dairy").IsExpanded.Should().BeFalse("the search didn't show it");
    }

    [Fact]
    public void A_material_keeps_its_row_through_a_refresh_and_takes_it_to_its_new_folder()
    {
        var tree = new RawMaterialTree();
        var flour = MaterialOf(1, "Bread Flour", Flour);
        tree.Refresh(Categories, [flour], isFiltered: false);
        var row = tree.Find(1)!;

        tree.Refresh(Categories, [MaterialOf(1, "Bread Flour", Dairy)], isFiltered: false);

        tree.Find(1).Should().BeSameAs(row);
        Folder(tree, RawMaterialType.Ingredient, "Dairy").Materials.Should().Equal(row);
        Folder(tree, RawMaterialType.Ingredient, "Flour").Materials.Should().BeEmpty();
    }

    [Fact]
    public void A_category_whose_type_changed_moves_its_folder_to_the_other_type()
    {
        var tree = new RawMaterialTree();
        var labels = new RawMaterialCategory { Id = 4, Name = "Labels", Type = RawMaterialCategoryType.Ingredient };
        tree.Refresh([Flour, labels], [], isFiltered: false);

        tree.Refresh([Flour, new RawMaterialCategory { Id = 4, Name = "Labels", Type = RawMaterialCategoryType.Packaging }], [], isFiltered: false);

        TitlesIn(tree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour");
        TitlesIn(tree.TypeFolder(RawMaterialType.Packaging)).Should().Equal("Labels");
        tree.CategoryFolder(4)!.Type.Should().Be(RawMaterialType.Packaging);
    }

    [Fact]
    public void A_deleted_categorys_folder_goes()
    {
        var tree = new RawMaterialTree();
        tree.Refresh(Categories, [], isFiltered: false);

        tree.Refresh([Flour, Boxes], [], isFiltered: false);

        TitlesIn(tree.TypeFolder(RawMaterialType.Ingredient)).Should().Equal("Flour");
        tree.CategoryFolder(Dairy.Id).Should().BeNull();
    }

    [Fact]
    public void A_drop_moves_a_material_only_into_another_category_of_its_own_type()
    {
        var tree = new RawMaterialTree();
        var breadFlour = MaterialOf(1, "Bread Flour", Flour);
        var butter = MaterialOf(2, "Butter", Dairy);
        var cakeBox = MaterialOf(3, "Cake Box", Boxes);
        tree.Refresh(Categories, [breadFlour, butter, cakeBox], isFiltered: false);

        RawMaterialTree.MoveTo(Folder(tree, RawMaterialType.Ingredient, "Dairy"), breadFlour).Should().Be(new RawMaterialMove(1, Dairy.Id));
        RawMaterialTree.MoveTo(tree.Find(2), breadFlour).Should().Be(new RawMaterialMove(1, Dairy.Id), "onto a material is into its folder");

        RawMaterialTree.MoveTo(Folder(tree, RawMaterialType.Ingredient, "Flour"), breadFlour).Should().BeNull("it's there already");
        RawMaterialTree.MoveTo(tree.TypeFolder(RawMaterialType.Ingredient), breadFlour).Should().BeNull("a type's folder has no category to give");
        RawMaterialTree.MoveTo(Folder(tree, RawMaterialType.Packaging, "Boxes"), breadFlour).Should().BeNull("an ingredient can't be packaging");
        RawMaterialTree.MoveTo(tree.Find(3), breadFlour).Should().BeNull();
        RawMaterialTree.MoveTo(null, breadFlour).Should().BeNull();
    }

    [Fact]
    public void Several_materials_move_together_leaving_out_those_that_would_not_move()
    {
        RawMaterial[] materials = [MaterialOf(1, "Bread Flour", Flour), MaterialOf(2, "Butter", Dairy), MaterialOf(3, "Cake Box", Boxes)];

        RawMaterialTree.MovesTo(Dairy, materials).Should().Equal(new RawMaterialMove(1, Dairy.Id));
        RawMaterialTree.MovesTo(Boxes, materials).Should().BeEmpty();
    }

    [Fact]
    public void The_folders_left_open_start_open_and_a_folder_with_its_first_rows_is_announced_open_again()
    {
        var tree = new RawMaterialTree(["Ingredient", $"Ingredient/{Flour.Id}"]);
        var flourFolderAnnounced = 0;

        // The categories come in first, as a load merges them, so the folder is made open but empty.
        tree.Refresh(Categories, [], isFiltered: false);
        var flour = Folder(tree, RawMaterialType.Ingredient, "Flour");
        flour.PropertyChanged += (_, e) => flourFolderAnnounced += e.PropertyName == nameof(RawMaterialFolderNode.IsExpanded) ? 1 : 0;

        tree.Refresh(Categories, [MaterialOf(1, "Bread Flour", Flour)], isFiltered: false);

        flour.IsExpanded.Should().BeTrue();
        flourFolderAnnounced.Should().Be(1, "the tree can't open a folder with nothing in it, and has to be told again once it has rows");
        tree.TypeFolder(RawMaterialType.Packaging).IsExpanded.Should().BeFalse("it wasn't left open");
        tree.OpenFolderKeys().Should().Equal("Ingredient", $"Ingredient/{Flour.Id}");
    }

    [Fact]
    public void An_open_type_folder_is_announced_open_again_once_it_has_rows()
    {
        // The tree on the screen showing at startup binds to the type folders before the first load fills them, and can't
        // open a folder with nothing in it: without this the list opened on its two type folders closed.
        var tree = new RawMaterialTree();
        var ingredients = tree.TypeFolder(RawMaterialType.Ingredient);
        var announced = 0;
        ingredients.PropertyChanged += (_, e) => announced += e.PropertyName == nameof(RawMaterialFolderNode.IsExpanded) ? 1 : 0;

        tree.Refresh(Categories, [], isFiltered: false);

        announced.Should().Be(1);

        tree.Refresh(Categories, [MaterialOf(1, "Bread Flour", Flour)], isFiltered: false);

        announced.Should().Be(1, "it had rows already");
    }

    [Fact]
    public void Expand_All_and_Collapse_All_work_on_one_type_or_both()
    {
        var tree = new RawMaterialTree();
        tree.Refresh(Categories, [], isFiltered: false);

        tree.ExpandAll(tree.TypeFolder(RawMaterialType.Packaging));
        Folder(tree, RawMaterialType.Packaging, "Boxes").IsExpanded.Should().BeTrue();
        Folder(tree, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeFalse();

        tree.ExpandAll(null);
        Folder(tree, RawMaterialType.Ingredient, "Flour").IsExpanded.Should().BeTrue();

        tree.CollapseAll(null);
        Categories.Select(c => tree.CategoryFolder(c.Id)!).Should().OnlyContain(f => !f.IsExpanded);
        tree.Roots.Should().OnlyContain(r => r.IsExpanded, "the types stay open, as the list first opens");
    }
}
