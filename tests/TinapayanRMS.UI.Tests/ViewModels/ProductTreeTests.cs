using System.Collections.Specialized;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The Products list's tree: a folder per stage, holding a folder per category and Uncategorized, holding products; then
/// the Deleted folder, holding the deleted products.
/// </summary>
public class ProductTreeTests
{
    private static readonly ProductCategory Breads = new() { Id = 1, Name = "Breads" };
    private static readonly ProductCategory Cakes = new() { Id = 2, Name = "Cakes" };

    private static Product ProductOf(int id, string name, ProductStage stage, ProductCategory? category) =>
        new() { Id = id, Name = name, Stage = stage, CategoryId = category?.Id, Category = category };

    private static ProductFolderNode Root(ProductTree tree, ProductStage stage) => tree.Stages.Single(r => r.Stage == stage);

    private static Product Deleted(Product product)
    {
        product.DeletedAt = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Local);
        return product;
    }

    private static ProductFolderNode Folder(ProductTree tree, ProductStage stage, string title) =>
        Root(tree, stage).Children.OfType<ProductFolderNode>().Single(f => f.Title == title);

    private static IEnumerable<string> TitlesIn(ProductFolderNode folder) => folder.Children.Select(c => c.Title);

    [Theory]
    [InlineData("Breads", ProductIcon.Breads)]
    [InlineData("Loaves", ProductIcon.Loaves)]
    [InlineData("Cakes", ProductIcon.Cakes)]
    [InlineData("Pastries", ProductIcon.Pastries)]
    [InlineData(" pastries ", ProductIcon.Pastries)]
    [InlineData("Rolls", ProductIcon.Rolls)]
    [InlineData("Cookies", ProductIcon.None)]
    public void A_products_icon_is_its_categorys(string category, ProductIcon kind) =>
        new ProductLeafNode(ProductOf(1, "Pandesal", ProductStage.Production, new ProductCategory { Id = 9, Name = category }))
            .IconKind.Should().Be(kind);

    [Fact]
    public void A_composite_has_its_own_icon_whatever_its_category_one_without_a_category_has_none_and_a_folder_never_does()
    {
        var composite = ProductOf(1, "Pasalubong Box", ProductStage.Production, Breads);
        composite.IsComposite = true;
        var uncategorized = new ProductLeafNode(ProductOf(2, "Pandesal", ProductStage.Production, null));

        new ProductLeafNode(composite).IconKind.Should().Be(ProductIcon.Composite);
        uncategorized.IconKind.Should().Be(ProductIcon.None);
        ProductFolderNode.ForStage(ProductStage.Production).IconKind.Should().Be(ProductIcon.None);

        // A refresh with the product recategorized keeps its row, which takes its new category's picture.
        var tree = new ProductTree();
        tree.Refresh([Cakes], [ProductOf(2, "Pandesal", ProductStage.Production, null)], isFiltered: false);
        var leaf = Folder(tree, ProductStage.Production, ProductRules.UncategorizedTitle).Children.OfType<ProductLeafNode>().Single();

        tree.Refresh([Cakes], [ProductOf(2, "Pandesal", ProductStage.Production, Cakes)], isFiltered: false);

        Folder(tree, ProductStage.Production, "Cakes").Children.Should().ContainSingle().Which.Should().BeSameAs(leaf);
        leaf.IconKind.Should().Be(ProductIcon.Cakes);
    }

    [Fact]
    public void The_stages_are_the_top_level_folders_Production_first_then_Deleted()
    {
        var tree = new ProductTree();

        tree.Roots.Select(r => r.Title).Should().Equal("Production", "Research and Development", "Deleted");
        tree.Stages.Should().Equal(tree.Roots.Take(2));
        tree.Stages.Should().OnlyContain(r => r.IsStage && r.IsTopLevel && r.CategoryId == null && !r.IsDeletedFolder);
        tree.DeletedFolder.Should().BeSameAs(tree.Roots[2]);
        tree.DeletedFolder.Should().Match<ProductFolderNode>(f => f.IsDeletedFolder && f.IsTopLevel && !f.IsStage && !f.IsCategory);
        tree.DeletedFolder.Key.Should().Be("Deleted");
        tree.DeletedFolder.IsExpanded.Should().BeFalse("it starts closed");
    }

    [Fact]
    public void The_Deleted_folder_holds_the_deleted_products_by_name_whatever_their_stage_and_category()
    {
        var tree = new ProductTree();
        var pandesal = ProductOf(1, "Pandesal", ProductStage.Production, Breads);
        var mamon = Deleted(ProductOf(2, "Mamon", ProductStage.Production, Cakes));
        var ensaymada = Deleted(ProductOf(3, "Ensaymada", ProductStage.ResearchAndDevelopment, null));

        tree.Refresh([Breads, Cakes], [pandesal], isFiltered: false, [ensaymada, mamon]);

        TitlesIn(tree.DeletedFolder).Should().Equal("Ensaymada", "Mamon");
        tree.DeletedFolder.ProductCount.Should().Be(2);
        tree.DeletedFolder.Glyph.Should().Be("\ue74d", "the bin, open or not");
        TitlesIn(Folder(tree, ProductStage.Production, "Cakes")).Should().BeEmpty();
        Root(tree, ProductStage.Production).ProductCount.Should().Be(1, "a stage counts only its own");
        tree.Find(2)!.IsDeleted.Should().BeTrue();
        tree.Find(1)!.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void A_product_deleted_or_restored_keeps_its_row_which_moves_to_and_from_the_Deleted_folder()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(2, "Mamon", ProductStage.Production, Cakes);
        tree.Refresh([Cakes], [mamon], isFiltered: false);
        var leaf = tree.Find(2)!;

        tree.Refresh([Cakes], [], isFiltered: false, [Deleted(mamon)]);

        tree.Find(2).Should().BeSameAs(leaf);
        tree.DeletedFolder.Children.Should().Equal(leaf);
        Folder(tree, ProductStage.Production, "Cakes").Children.Should().BeEmpty();

        tree.Refresh([Cakes], [mamon], isFiltered: false, []);

        tree.Find(2).Should().BeSameAs(leaf);
        tree.DeletedFolder.Children.Should().BeEmpty();
        Folder(tree, ProductStage.Production, "Cakes").Children.Should().Equal(leaf);
    }

    [Fact]
    public void A_search_opens_the_Deleted_folder_on_its_matches()
    {
        var tree = new ProductTree();

        tree.Refresh([Cakes], [], isFiltered: true, []);
        tree.DeletedFolder.IsExpanded.Should().BeFalse("nothing in it matches");

        tree.Refresh([Cakes], [], isFiltered: true, [Deleted(ProductOf(2, "Mamon", ProductStage.Production, Cakes))]);
        tree.DeletedFolder.IsExpanded.Should().BeTrue();
    }

    [Fact]
    public void A_deleted_product_moves_nowhere_and_nothing_moves_into_the_Deleted_folder()
    {
        var tree = new ProductTree();
        var pandesal = ProductOf(1, "Pandesal", ProductStage.Production, Breads);
        var mamon = Deleted(ProductOf(2, "Mamon", ProductStage.Production, Cakes));
        tree.Refresh([Breads, Cakes], [pandesal], isFiltered: false, [mamon]);

        ProductTree.MoveTo(Folder(tree, ProductStage.ResearchAndDevelopment, "Breads"), mamon).Should().BeNull();
        ProductTree.MoveTo(tree.DeletedFolder, pandesal).Should().BeNull();
        ProductTree.MoveTo(tree.Find(2), pandesal).Should().BeNull("dropped on a deleted product is dropped in Deleted");
    }

    [Fact]
    public void Each_stage_holds_every_category_by_name_then_Uncategorized_and_each_folder_its_products()
    {
        var tree = new ProductTree();

        tree.Refresh(
            [Breads, Cakes],
            [
                ProductOf(1, "Ensaymada", ProductStage.Production, Breads),
                ProductOf(2, "Mamon", ProductStage.ResearchAndDevelopment, Cakes),
                ProductOf(3, "Pandesal", ProductStage.Production, Breads),
                ProductOf(4, "Test Loaf", ProductStage.ResearchAndDevelopment, null),
            ],
            isFiltered: false);

        foreach (var root in tree.Stages)
        {
            TitlesIn(root).Should().Equal("Breads", "Cakes", "Uncategorized");
        }

        TitlesIn(Folder(tree, ProductStage.Production, "Breads")).Should().Equal("Ensaymada", "Pandesal");
        TitlesIn(Folder(tree, ProductStage.Production, "Cakes")).Should().BeEmpty("an empty folder still shows, to be dropped on");
        TitlesIn(Folder(tree, ProductStage.ResearchAndDevelopment, "Cakes")).Should().Equal("Mamon");
        TitlesIn(Folder(tree, ProductStage.ResearchAndDevelopment, "Uncategorized")).Should().Equal("Test Loaf");
    }

    [Fact]
    public void A_search_shows_only_the_folders_holding_a_match()
    {
        var tree = new ProductTree();

        tree.Refresh([Breads, Cakes], [ProductOf(1, "Pandesal", ProductStage.Production, Breads)], isFiltered: true);

        TitlesIn(Root(tree, ProductStage.Production)).Should().Equal("Breads");
        TitlesIn(Root(tree, ProductStage.ResearchAndDevelopment)).Should().BeEmpty("the stages always show, even with no match");
    }

    [Fact]
    public void The_stages_start_open_and_their_folders_closed_and_a_search_opens_the_folders_it_shows()
    {
        var tree = new ProductTree();
        Product[] products = [ProductOf(1, "Pandesal", ProductStage.Production, Breads)];
        tree.Refresh([Breads, Cakes], products, isFiltered: false);

        tree.Stages.Should().OnlyContain(r => r.IsExpanded);
        Folder(tree, ProductStage.Production, "Breads").IsExpanded.Should().BeFalse();

        tree.Refresh([Breads, Cakes], products, isFiltered: true);
        Folder(tree, ProductStage.Production, "Breads").IsExpanded.Should().BeTrue("every match shows");

        tree.Refresh([Breads, Cakes], products, isFiltered: false);
        Folder(tree, ProductStage.Production, "Breads").IsExpanded.Should().BeTrue("a folder stays as it was left");
        Folder(tree, ProductStage.Production, "Cakes").IsExpanded.Should().BeFalse("the search didn't show it");
    }

    [Fact]
    public void A_folder_shows_the_open_folder_glyph_only_while_it_is_open_on_its_rows()
    {
        const string folderGlyph = "";
        const string openFolderGlyph = "";
        var tree = new ProductTree();
        tree.Refresh([Breads, Cakes], [ProductOf(1, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);
        var breads = Folder(tree, ProductStage.Production, "Breads");
        var glyphChanges = 0;
        breads.PropertyChanged += (_, e) => glyphChanges += e.PropertyName == nameof(ProductFolderNode.Glyph) ? 1 : 0;

        breads.Glyph.Should().Be(folderGlyph);
        breads.IsExpanded = true;
        breads.Glyph.Should().Be(openFolderGlyph);
        glyphChanges.Should().BeGreaterThan(0, "the row's icon follows it");

        breads.IsExpanded = false;
        breads.Glyph.Should().Be(folderGlyph);

        var cakes = Folder(tree, ProductStage.Production, "Cakes");
        cakes.IsExpanded = true;
        cakes.Glyph.Should().Be(folderGlyph, "an empty folder has nothing to show open");
        tree.Refresh([Breads, Cakes], [ProductOf(1, "Pandesal", ProductStage.Production, Cakes)], isFiltered: false);
        cakes.Glyph.Should().Be(openFolderGlyph, "a product moved in fills it");
    }

    [Fact]
    public void An_open_folder_a_search_hid_tells_the_tree_it_is_open_when_it_shows_again()
    {
        var tree = new ProductTree();
        var pandesal = ProductOf(1, "Pandesal", ProductStage.Production, Breads);
        var mamon = ProductOf(2, "Mamon", ProductStage.Production, Cakes);
        tree.Refresh([Breads, Cakes], [pandesal, mamon], isFiltered: false);
        var cakes = Folder(tree, ProductStage.Production, "Cakes");
        cakes.IsExpanded = true;
        tree.Refresh([Breads, Cakes], [pandesal], isFiltered: true);
        Root(tree, ProductStage.Production).Children.Should().NotContain(cakes, "the search hides it");
        var told = new List<string?>();
        cakes.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        tree.Refresh([Breads, Cakes], [pandesal, mamon], isFiltered: false);

        Root(tree, ProductStage.Production).Children.Should().Contain(cakes);
        cakes.IsExpanded.Should().BeTrue();
        told.Should().Contain(nameof(ProductFolderNode.IsExpanded), "the tree doesn't read it for a folder shown again unless told");
    }

    [Fact]
    public void An_open_folder_that_gets_its_first_product_tells_the_tree_it_is_open()
    {
        // As the screen loads: the categories arrive first, so each folder is made empty, and its products after.
        var tree = new ProductTree(["Production", "Production/2"]);
        tree.Refresh([Breads, Cakes], [], isFiltered: false);
        var cakes = Folder(tree, ProductStage.Production, "Cakes");
        var breads = Folder(tree, ProductStage.Production, "Breads");
        var told = new List<string?>();
        cakes.PropertyChanged += (_, e) => told.Add(e.PropertyName);
        var breadsTold = new List<string?>();
        breads.PropertyChanged += (_, e) => breadsTold.Add(e.PropertyName);

        tree.Refresh([Breads, Cakes], [ProductOf(1, "Mamon", ProductStage.Production, Cakes), ProductOf(2, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);

        told.Should().Contain(nameof(ProductFolderNode.IsExpanded), "the tree can't open a folder with nothing in it, and doesn't look again when rows arrive");
        breadsTold.Should().NotContain(nameof(ProductFolderNode.IsExpanded), "a closed one has nothing to tell");
    }

    [Fact]
    public void Refresh_keeps_every_node_and_takes_a_fresh_copy_of_a_product_into_its_row()
    {
        var tree = new ProductTree();
        tree.Refresh([Breads], [ProductOf(1, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);
        var folder = Folder(tree, ProductStage.Production, "Breads");
        var leaf = tree.Find(1)!;
        var titleChanges = new List<string?>();
        leaf.PropertyChanged += (_, e) => titleChanges.Add(e.PropertyName);

        var renamed = ProductOf(1, "Pandesal Special", ProductStage.Production, Breads);
        tree.Refresh([new ProductCategory { Id = 1, Name = "Bread" }], [renamed], isFiltered: false);

        tree.Find(1).Should().BeSameAs(leaf);
        leaf.Product.Should().BeSameAs(renamed);
        leaf.Title.Should().Be("Pandesal Special");
        titleChanges.Should().Contain(nameof(ProductLeafNode.Title));
        Folder(tree, ProductStage.Production, "Bread").Should().BeSameAs(folder, "a renamed category keeps its folder");
    }

    [Fact]
    public void A_product_moved_to_another_stage_and_category_takes_its_row_to_the_new_folder()
    {
        var tree = new ProductTree();
        tree.Refresh([Breads, Cakes], [ProductOf(1, "Mamon", ProductStage.ResearchAndDevelopment, Breads)], isFiltered: false);
        var leaf = tree.Find(1)!;
        var from = Folder(tree, ProductStage.ResearchAndDevelopment, "Breads");
        var to = Folder(tree, ProductStage.Production, "Cakes");
        var arrivedWhileStillInOld = false;
        ((INotifyCollectionChanged)to.Children).CollectionChanged += (_, _) => arrivedWhileStillInOld |= from.Children.Contains(leaf);

        tree.Refresh([Breads, Cakes], [ProductOf(1, "Mamon", ProductStage.Production, Cakes)], isFiltered: false);

        from.Children.Should().BeEmpty();
        to.Children.Should().ContainSingle().Which.Should().BeSameAs(leaf);
        arrivedWhileStillInOld.Should().BeFalse("a row leaves its old folder before it arrives in the new one");
    }

    [Fact]
    public void A_deleted_category_takes_its_folders_and_its_products_go_to_Uncategorized()
    {
        var tree = new ProductTree();
        tree.Refresh([Breads, Cakes], [ProductOf(1, "Mamon", ProductStage.Production, Cakes)], isFiltered: false);

        tree.Refresh([Breads], [ProductOf(1, "Mamon", ProductStage.Production, null)], isFiltered: false);

        TitlesIn(Root(tree, ProductStage.Production)).Should().Equal("Breads", "Uncategorized");
        TitlesIn(Folder(tree, ProductStage.Production, "Uncategorized")).Should().Equal("Mamon");
    }

    [Fact]
    public void A_category_read_with_a_product_but_not_yet_listed_still_gets_its_folder()
    {
        var tree = new ProductTree();

        tree.Refresh([Breads], [ProductOf(1, "Mamon", ProductStage.Production, Cakes)], isFiltered: false);

        TitlesIn(Folder(tree, ProductStage.Production, "Cakes")).Should().Equal("Mamon");
    }

    [Fact]
    public void A_product_no_longer_listed_loses_its_row()
    {
        var tree = new ProductTree();
        tree.Refresh([Breads], [ProductOf(1, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);

        tree.Refresh([Breads], [], isFiltered: false);

        tree.Find(1).Should().BeNull();
        Folder(tree, ProductStage.Production, "Breads").Children.Should().BeEmpty();
    }

    [Fact]
    public void MoveTo_a_category_folder_files_the_product_under_its_stage_and_category()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.ResearchAndDevelopment, Breads);
        tree.Refresh([Breads, Cakes], [mamon], isFiltered: false);

        ProductTree.MoveTo(Folder(tree, ProductStage.Production, "Cakes"), mamon)
            .Should().Be(new ProductMove(1, ProductStage.Production, Cakes.Id));
        ProductTree.MoveTo(Folder(tree, ProductStage.ResearchAndDevelopment, "Uncategorized"), mamon)
            .Should().Be(new ProductMove(1, ProductStage.ResearchAndDevelopment, null));
    }

    [Fact]
    public void MoveTo_a_stage_folder_changes_only_the_stage()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.ResearchAndDevelopment, Breads);
        tree.Refresh([Breads], [mamon], isFiltered: false);

        ProductTree.MoveTo(Root(tree, ProductStage.Production), mamon).Should().Be(new ProductMove(1, ProductStage.Production, Breads.Id));
    }

    [Fact]
    public void MoveTo_another_product_files_it_in_that_products_folder()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.ResearchAndDevelopment, Breads);
        var sansRival = ProductOf(2, "Sans Rival", ProductStage.Production, Cakes);
        tree.Refresh([Breads, Cakes], [mamon, sansRival], isFiltered: false);

        ProductTree.MoveTo(tree.Find(2), mamon).Should().Be(new ProductMove(1, ProductStage.Production, Cakes.Id));
    }

    [Fact]
    public void MoveTo_where_the_product_is_already_or_nowhere_is_no_move()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.ResearchAndDevelopment, Breads);
        var ensaymada = ProductOf(2, "Ensaymada", ProductStage.ResearchAndDevelopment, Breads);
        tree.Refresh([Breads], [mamon, ensaymada], isFiltered: false);

        ProductTree.MoveTo(Folder(tree, ProductStage.ResearchAndDevelopment, "Breads"), mamon).Should().BeNull();
        ProductTree.MoveTo(Root(tree, ProductStage.ResearchAndDevelopment), mamon).Should().BeNull();
        ProductTree.MoveTo(tree.Find(2), mamon).Should().BeNull();
        ProductTree.MoveTo(null, mamon).Should().BeNull();
    }

    [Fact]
    public void Each_folder_counts_the_products_it_shows_and_each_stage_all_of_its_own()
    {
        var tree = new ProductTree();
        Product[] products =
        [
            ProductOf(1, "Ensaymada", ProductStage.Production, Breads),
            ProductOf(2, "Pandesal", ProductStage.Production, Breads),
            ProductOf(3, "Mamon", ProductStage.Production, Cakes),
            ProductOf(4, "Test Loaf", ProductStage.ResearchAndDevelopment, null),
        ];

        tree.Refresh([Breads, Cakes], products, isFiltered: false);

        Folder(tree, ProductStage.Production, "Breads").ProductCount.Should().Be(2);
        Folder(tree, ProductStage.Production, "Cakes").ProductCount.Should().Be(1);
        Folder(tree, ProductStage.Production, "Uncategorized").ProductCount.Should().Be(0, "an empty folder says so");
        Root(tree, ProductStage.Production).ProductCount.Should().Be(3);
        Root(tree, ProductStage.ResearchAndDevelopment).ProductCount.Should().Be(1);
        tree.Find(1)!.ProductCount.Should().Be(0, "a product counts nothing");

        tree.Refresh([Breads, Cakes], [products[1]], isFiltered: true);
        Folder(tree, ProductStage.Production, "Breads").ProductCount.Should().Be(1, "with a search on, a folder counts its matches");
        Root(tree, ProductStage.Production).ProductCount.Should().Be(1);
    }

    [Fact]
    public void Expand_All_opens_every_folder_in_the_stage_and_Collapse_All_closes_them_but_leaves_the_stage_open()
    {
        var tree = new ProductTree();
        tree.Refresh([Breads, Cakes], [], isFiltered: false);
        var production = Root(tree, ProductStage.Production);
        production.IsExpanded = false;

        tree.ExpandAll(production);

        production.IsExpanded.Should().BeTrue();
        production.Children.Cast<ProductFolderNode>().Should().OnlyContain(f => f.IsExpanded);
        Root(tree, ProductStage.ResearchAndDevelopment).Children.Cast<ProductFolderNode>().Should().OnlyContain(f => !f.IsExpanded, "the other stage is left alone");

        tree.ExpandAll(null);
        tree.Stages.SelectMany(r => r.Children).Cast<ProductFolderNode>().Should().OnlyContain(f => f.IsExpanded, "null is both stages");
        tree.DeletedFolder.IsExpanded.Should().BeTrue("and the Deleted folder");

        tree.CollapseAll(null);
        tree.Stages.SelectMany(r => r.Children).Cast<ProductFolderNode>().Should().OnlyContain(f => !f.IsExpanded);
        tree.Stages.Should().OnlyContain(r => r.IsExpanded, "the stages stay open on their folders");
        tree.DeletedFolder.IsExpanded.Should().BeFalse();
    }

    [Fact]
    public void The_folders_left_open_open_again_and_the_rest_start_closed()
    {
        var first = new ProductTree();
        first.Refresh([Breads, Cakes], [], isFiltered: false);
        Folder(first, ProductStage.Production, "Cakes").IsExpanded = true;
        Folder(first, ProductStage.ResearchAndDevelopment, "Uncategorized").IsExpanded = true;
        Root(first, ProductStage.ResearchAndDevelopment).IsExpanded = false;

        var openFolders = first.OpenFolderKeys();

        openFolders.Should().BeEquivalentTo("Production", "Production/2", "ResearchAndDevelopment/Uncategorized");

        var next = new ProductTree(openFolders);
        next.Refresh([Breads, Cakes], [], isFiltered: false);
        Root(next, ProductStage.Production).IsExpanded.Should().BeTrue();
        Root(next, ProductStage.ResearchAndDevelopment).IsExpanded.Should().BeFalse("a stage left closed starts closed");
        Folder(next, ProductStage.Production, "Cakes").IsExpanded.Should().BeTrue("a category's folder made on the first Refresh opens as it was left");
        Folder(next, ProductStage.Production, "Breads").IsExpanded.Should().BeFalse();
        Folder(next, ProductStage.ResearchAndDevelopment, "Uncategorized").IsExpanded.Should().BeTrue();
        next.OpenFolderKeys().Should().Equal(openFolders);
    }

    [Fact]
    public void A_folder_opened_or_closed_is_reported()
    {
        var tree = new ProductTree();
        tree.Refresh([Breads], [], isFiltered: false);
        var reports = 0;
        using var subscription = tree.ExpansionChanged.Subscribe(_ => reports++);

        Folder(tree, ProductStage.Production, "Breads").IsExpanded = true;
        Root(tree, ProductStage.ResearchAndDevelopment).IsExpanded = false;
        tree.Refresh([Breads, Cakes], [], isFiltered: false);
        Folder(tree, ProductStage.Production, "Cakes").IsExpanded = true;

        reports.Should().Be(3, "a folder made since reports too");
    }

    [Fact]
    public void MovesTo_several_products_leaves_out_the_ones_there_already()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.ResearchAndDevelopment, Breads);
        var pandesal = ProductOf(2, "Pandesal", ProductStage.Production, Breads);
        var sansRival = ProductOf(3, "Sans Rival", ProductStage.ResearchAndDevelopment, Cakes);
        tree.Refresh([Breads, Cakes], [mamon, pandesal, sansRival], isFiltered: false);

        ProductTree.MovesTo(Folder(tree, ProductStage.Production, "Breads"), [mamon, pandesal, sansRival])
            .Should().Equal(new ProductMove(1, ProductStage.Production, Breads.Id), new ProductMove(3, ProductStage.Production, Breads.Id));

        ProductTree.MovesTo(ProductStage.Production, null, keepCategory: true, [mamon, pandesal, sansRival])
            .Should().Equal([new ProductMove(1, ProductStage.Production, Breads.Id), new ProductMove(3, ProductStage.Production, Cakes.Id)], "a Promote keeps each one's category");
        ProductTree.MovesTo(ProductStage.ResearchAndDevelopment, null, keepCategory: false, [mamon, sansRival])
            .Should().Equal(new ProductMove(1, ProductStage.ResearchAndDevelopment, null), new ProductMove(3, ProductStage.ResearchAndDevelopment, null));
        ProductTree.MovesTo(ProductStage.Production, Breads.Id, keepCategory: false, [pandesal]).Should().BeEmpty();
    }
}
