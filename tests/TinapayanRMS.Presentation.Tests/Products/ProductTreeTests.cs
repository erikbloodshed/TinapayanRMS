using System.Collections.Specialized;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.Products;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.Trees;

namespace TinapayanRMS.Presentation.Tests.Products;

/// <summary>
/// The Products list's tree: a folder per stage, holding the user's folders, as deep as they go, and the products at the
/// top of the stage; each folder its own folders, then its products; then the Deleted folder, holding the deleted products.
/// </summary>
public class ProductTreeTests
{
    // Production › Breads › Holiday, Production › Cakes, and Innovation › Trials.
    private static ProductFolder Breads => new() { Id = 1, Name = "Breads", Stage = ProductStage.Production };
    private static ProductFolder Cakes => new() { Id = 2, Name = "Cakes", Stage = ProductStage.Production };
    private static ProductFolder Holiday => new() { Id = 3, Name = "Holiday", Stage = ProductStage.Production, ParentId = 1 };
    private static ProductFolder Trials => new() { Id = 4, Name = "Trials", Stage = ProductStage.Innovation };

    private static ProductFolder[] AllFolders => [Breads, Cakes, Holiday, Trials];

    private static Product ProductOf(int id, string name, ProductStage stage, ProductFolder? folder = null) =>
        new() { Id = id, Name = name, Stage = stage, FolderId = folder?.Id };

    private static ProductFolderNode Root(ProductTree tree, ProductStage stage) => tree.Stages.Single(r => r.Stage == stage);

    private static ProductFolderNode Folder(ProductTree tree, int id) => tree.Folder(id)!;

    private static Product Deleted(Product product)
    {
        product.DeletedAt = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Local);
        return product;
    }

    private static IEnumerable<string> TitlesIn(ProductFolderNode folder) => folder.Children.Select(c => c.Title);

    [Test]
    public void The_stages_are_the_top_level_folders_Production_first_then_Deleted()
    {
        var tree = new ProductTree();

        tree.Roots.Select(r => r.Title).Should().Equal("Production", "Innovation", "Deleted");
        tree.Stages.Should().Equal(tree.Roots.Take(2));
        tree.Stages.Should().OnlyContain(r => r.IsStage && r.IsTopLevel && r.FolderId == null && !r.IsUserFolder && !r.IsDeletedFolder);
        tree.DeletedFolder.Should().BeSameAs(tree.Roots[2]);
        tree.DeletedFolder.Should().Match<ProductFolderNode>(f => f.IsDeletedFolder && f.IsTopLevel && !f.IsStage && !f.IsUserFolder);
        tree.DeletedFolder.Key.Should().Be("Deleted");
        tree.DeletedFolder.IsExpanded.Should().BeFalse("it starts closed");
    }

    [Test]
    public void Each_stage_holds_its_folders_by_name_then_its_own_products_and_each_folder_its_folders_then_its_products()
    {
        var tree = new ProductTree();

        tree.Refresh(
            AllFolders,
            [
                ProductOf(1, "Pandesal", ProductStage.Production, Breads),
                ProductOf(2, "Bibingka", ProductStage.Production, Holiday),
                ProductOf(3, "Ensaymada", ProductStage.Production, Breads),
                ProductOf(4, "Loose Loaf", ProductStage.Production),
                ProductOf(5, "Test Bun", ProductStage.Innovation, Trials),
                ProductOf(6, "Apple Pie", ProductStage.Production),
            ],
            isFiltered: false);

        TitlesIn(Root(tree, ProductStage.Production)).Should().Equal("Breads", "Cakes", "Apple Pie", "Loose Loaf");
        TitlesIn(Folder(tree, Breads.Id)).Should().Equal("Holiday", "Ensaymada", "Pandesal");
        TitlesIn(Folder(tree, Holiday.Id)).Should().Equal("Bibingka");
        TitlesIn(Folder(tree, Cakes.Id)).Should().BeEmpty("an empty folder still shows, to be dropped on");
        TitlesIn(Root(tree, ProductStage.Innovation)).Should().Equal("Trials");
        Folder(tree, Holiday.Id).ParentFolder.Should().BeSameAs(Folder(tree, Breads.Id));
        Folder(tree, Breads.Id).ParentFolder.Should().BeSameAs(Root(tree, ProductStage.Production));
        Folder(tree, Holiday.Id).ParentFolderId.Should().Be(Breads.Id);
        Folder(tree, Breads.Id).ParentFolderId.Should().BeNull();
    }

    [Test]
    public void The_Deleted_folder_holds_the_deleted_products_by_name_whatever_their_stage_and_folder()
    {
        var tree = new ProductTree();
        var pandesal = ProductOf(1, "Pandesal", ProductStage.Production, Breads);
        var mamon = Deleted(ProductOf(2, "Mamon", ProductStage.Production, Cakes));
        var ensaymada = Deleted(ProductOf(3, "Ensaymada", ProductStage.Innovation));

        tree.Refresh(AllFolders, [pandesal], isFiltered: false, [ensaymada, mamon]);

        TitlesIn(tree.DeletedFolder).Should().Equal("Ensaymada", "Mamon");
        tree.DeletedFolder.Count.Should().Be(2);
        tree.DeletedFolder.Icon.Should().Be(AppIcon.Delete, "the bin, open or not");
        TitlesIn(Folder(tree, Cakes.Id)).Should().BeEmpty();
        Root(tree, ProductStage.Production).Count.Should().Be(1, "a stage counts only its own");
        tree.Find(2)!.IsDeleted.Should().BeTrue();
        tree.Find(1)!.IsDeleted.Should().BeFalse();
    }

    [Test]
    public void A_product_deleted_or_restored_keeps_its_row_which_moves_to_and_from_the_Deleted_folder()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(2, "Mamon", ProductStage.Production, Cakes);
        tree.Refresh(AllFolders, [mamon], isFiltered: false);
        var leaf = tree.Find(2)!;

        tree.Refresh(AllFolders, [], isFiltered: false, [Deleted(mamon)]);

        tree.Find(2).Should().BeSameAs(leaf);
        tree.DeletedFolder.Children.Should().Equal(leaf);
        Folder(tree, Cakes.Id).Children.Should().BeEmpty();

        mamon.DeletedAt = null;
        tree.Refresh(AllFolders, [mamon], isFiltered: false, []);

        tree.Find(2).Should().BeSameAs(leaf);
        tree.DeletedFolder.Children.Should().BeEmpty();
        Folder(tree, Cakes.Id).Children.Should().Equal(leaf);
    }

    [Test]
    public void A_filter_opens_the_Deleted_folder_on_its_matches()
    {
        var tree = new ProductTree();

        tree.Refresh(AllFolders, [], isFiltered: true, []);
        tree.DeletedFolder.IsExpanded.Should().BeFalse("nothing in it matches");

        tree.Refresh(AllFolders, [], isFiltered: true, [Deleted(ProductOf(2, "Mamon", ProductStage.Production, Cakes))]);
        tree.DeletedFolder.IsExpanded.Should().BeTrue();
    }

    [Test]
    public void A_deleted_product_moves_nowhere_and_nothing_moves_into_the_Deleted_folder()
    {
        var tree = new ProductTree();
        var pandesal = ProductOf(1, "Pandesal", ProductStage.Production, Breads);
        var mamon = Deleted(ProductOf(2, "Mamon", ProductStage.Production, Cakes));
        tree.Refresh(AllFolders, [pandesal], isFiltered: false, [mamon]);

        ProductTree.MoveTo(Folder(tree, Trials.Id), mamon).Should().BeNull();
        ProductTree.MoveTo(tree.DeletedFolder, pandesal).Should().BeNull();
        ProductTree.MoveTo(tree.Find(2), pandesal).Should().BeNull("dropped on a deleted product is dropped in Deleted");
    }

    [Test]
    public void A_filter_shows_only_the_folders_holding_a_match_however_deep_opened()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [], isFiltered: false);
        tree.CollapseAll(null);

        tree.Refresh(AllFolders, [ProductOf(1, "Bibingka", ProductStage.Production, Holiday)], isFiltered: true);

        TitlesIn(Root(tree, ProductStage.Production)).Should().Equal("Breads");
        TitlesIn(Folder(tree, Breads.Id)).Should().Equal(["Holiday"], "Breads holds the match, inside Holiday");
        Folder(tree, Breads.Id).IsExpanded.Should().BeTrue("every match shows");
        Folder(tree, Holiday.Id).IsExpanded.Should().BeTrue();
        TitlesIn(Root(tree, ProductStage.Innovation)).Should().BeEmpty("the stages always show, even with no match");

        tree.Refresh(AllFolders, [ProductOf(1, "Bibingka", ProductStage.Production, Holiday)], isFiltered: false);
        Folder(tree, Holiday.Id).IsExpanded.Should().BeTrue("a folder stays as it was left");
        Folder(tree, Cakes.Id).IsExpanded.Should().BeFalse("the filter didn't show it");
    }

    [Test]
    public void Every_folder_starts_open_but_Deleted()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [ProductOf(1, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);

        tree.Stages.Should().OnlyContain(r => r.IsExpanded);
        AllFolders.Select(f => Folder(tree, f.Id)).Should().OnlyContain(f => f.IsExpanded);
        tree.DeletedFolder.IsExpanded.Should().BeFalse();
    }

    [Test]
    public void A_folder_shows_the_open_folder_icon_only_while_it_is_open_on_its_rows()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [ProductOf(1, "Pandesal", ProductStage.Production, Cakes)], isFiltered: false);
        tree.CollapseAll(null);
        var cakes = Folder(tree, Cakes.Id);
        var iconChanges = 0;
        cakes.PropertyChanged += (_, e) => iconChanges += e.PropertyName == nameof(ProductFolderNode.Icon) ? 1 : 0;

        cakes.Icon.Should().Be(AppIcon.Folder);
        cakes.IsExpanded = true;
        cakes.Icon.Should().Be(AppIcon.FolderOpen);
        iconChanges.Should().BeGreaterThan(0, "the row's icon follows it");

        cakes.IsExpanded = false;
        cakes.Icon.Should().Be(AppIcon.Folder);

        var trials = Folder(tree, Trials.Id);
        trials.IsExpanded = true;
        trials.Icon.Should().Be(AppIcon.Folder, "an empty folder has nothing to show open");
        tree.Refresh(AllFolders, [ProductOf(1, "Pandesal", ProductStage.Innovation, Trials)], isFiltered: false);
        trials.Icon.Should().Be(AppIcon.FolderOpen, "a product moved in fills it");
    }

    [Test]
    public void An_open_folder_a_filter_hid_tells_the_tree_it_is_open_when_it_shows_again()
    {
        var tree = new ProductTree();
        var pandesal = ProductOf(1, "Pandesal", ProductStage.Production, Breads);
        var mamon = ProductOf(2, "Mamon", ProductStage.Production, Cakes);
        tree.Refresh(AllFolders, [pandesal, mamon], isFiltered: false);
        var cakes = Folder(tree, Cakes.Id);
        cakes.IsExpanded = true;
        tree.Refresh(AllFolders, [pandesal], isFiltered: true);
        Root(tree, ProductStage.Production).Children.Should().NotContain(cakes, "the filter hides it");
        var told = new List<string?>();
        cakes.PropertyChanged += (_, e) => told.Add(e.PropertyName);

        tree.Refresh(AllFolders, [pandesal, mamon], isFiltered: false);

        Root(tree, ProductStage.Production).Children.Should().Contain(cakes);
        cakes.IsExpanded.Should().BeTrue();
        told.Should().Contain(nameof(ProductFolderNode.IsExpanded), "the tree doesn't read it for a folder shown again unless told");
    }

    [Test]
    public void An_open_folder_that_gets_its_first_product_tells_the_tree_it_is_open()
    {
        // As the screen loads: the folders may arrive before their products.
        var tree = new ProductTree(new FolderMemory([$"Folder/{Breads.Id}"]));
        tree.Refresh(AllFolders, [], isFiltered: false);
        var cakes = Folder(tree, Cakes.Id);
        var breads = Folder(tree, Breads.Id);
        var told = new List<string?>();
        cakes.PropertyChanged += (_, e) => told.Add(e.PropertyName);
        var breadsTold = new List<string?>();
        breads.PropertyChanged += (_, e) => breadsTold.Add(e.PropertyName);

        tree.Refresh(AllFolders, [ProductOf(1, "Mamon", ProductStage.Production, Cakes), ProductOf(2, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);

        told.Should().Contain(nameof(ProductFolderNode.IsExpanded), "the tree can't open a folder with nothing in it, and doesn't look again when rows arrive");
        breadsTold.Should().NotContain(nameof(ProductFolderNode.IsExpanded), "a closed one has nothing to tell");
    }

    [Test]
    public void Refresh_keeps_every_node_and_takes_a_fresh_copy_of_a_product_into_its_row()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [ProductOf(1, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);
        var folder = Folder(tree, Breads.Id);
        var leaf = tree.Find(1)!;
        var titleChanges = new List<string?>();
        leaf.PropertyChanged += (_, e) => titleChanges.Add(e.PropertyName);

        var renamed = ProductOf(1, "Pandesal Special", ProductStage.Production, Breads);
        var bread = Breads;
        bread.Name = "Bread";
        tree.Refresh([bread, Cakes, Holiday, Trials], [renamed], isFiltered: false);

        tree.Find(1).Should().BeSameAs(leaf);
        leaf.Product.Should().BeSameAs(renamed);
        leaf.Title.Should().Be("Pandesal Special");
        titleChanges.Should().Contain(nameof(ProductLeafNode.Title));
        Folder(tree, Breads.Id).Should().BeSameAs(folder, "a renamed folder keeps its row");
        folder.Title.Should().Be("Bread");
    }

    [Test]
    public void A_product_moved_to_another_stage_and_folder_takes_its_row_to_the_new_folder()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [ProductOf(1, "Mamon", ProductStage.Innovation, Trials)], isFiltered: false);
        var leaf = tree.Find(1)!;
        var from = Folder(tree, Trials.Id);
        var to = Folder(tree, Holiday.Id);
        var arrivedWhileStillInOld = false;
        ((INotifyCollectionChanged)to.Children).CollectionChanged += (_, _) => arrivedWhileStillInOld |= from.Children.Contains(leaf);

        tree.Refresh(AllFolders, [ProductOf(1, "Mamon", ProductStage.Production, Holiday)], isFiltered: false);

        from.Children.Should().BeEmpty();
        to.Children.Should().ContainSingle().Which.Should().BeSameAs(leaf);
        arrivedWhileStillInOld.Should().BeFalse("a row leaves its old folder before it arrives in the new one");
    }

    [Test]
    public void A_folder_moved_into_another_and_to_the_other_stage_keeps_its_row_with_all_it_holds()
    {
        var tree = new ProductTree();
        var bibingka = ProductOf(1, "Bibingka", ProductStage.Production, Holiday);
        tree.Refresh(AllFolders, [bibingka], isFiltered: false);
        var holiday = Folder(tree, Holiday.Id);
        var leaf = tree.Find(1)!;

        var movedHoliday = Holiday;
        movedHoliday.Stage = ProductStage.Innovation;
        movedHoliday.ParentId = Trials.Id;
        tree.Refresh([Breads, Cakes, movedHoliday, Trials], [ProductOf(1, "Bibingka", ProductStage.Innovation, Holiday)], isFiltered: false);

        Folder(tree, Holiday.Id).Should().BeSameAs(holiday);
        holiday.Stage.Should().Be(ProductStage.Innovation);
        holiday.ParentFolder.Should().BeSameAs(Folder(tree, Trials.Id));
        TitlesIn(Folder(tree, Trials.Id)).Should().Equal("Holiday");
        TitlesIn(Folder(tree, Breads.Id)).Should().BeEmpty();
        holiday.Children.Should().Equal(leaf);
        Folder(tree, Trials.Id).Count.Should().Be(1, "a folder counts what the folders in it hold");
        Root(tree, ProductStage.Production).Count.Should().Be(0);
    }

    [Test]
    public void A_folder_gone_loses_its_row_and_a_product_filed_in_a_folder_the_list_lacks_shows_at_the_top_of_its_stage()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [ProductOf(1, "Mamon", ProductStage.Production, Cakes)], isFiltered: false);

        tree.Refresh([Breads, Holiday, Trials], [ProductOf(1, "Mamon", ProductStage.Production, Cakes)], isFiltered: false);

        tree.Folder(Cakes.Id).Should().BeNull();
        TitlesIn(Root(tree, ProductStage.Production)).Should().Equal("Breads", "Mamon");
        tree.ClosedFolderKeys().Should().NotContain($"Folder/{Cakes.Id}");
    }

    [Test]
    public void A_loop_of_folders_left_by_an_edit_outside_the_app_shows_at_the_top_of_the_stage()
    {
        var tree = new ProductTree();
        ProductFolder a = new() { Id = 10, Name = "A", Stage = ProductStage.Production, ParentId = 11 };
        ProductFolder b = new() { Id = 11, Name = "B", Stage = ProductStage.Production, ParentId = 10 };

        tree.Refresh([a, b], [ProductOf(1, "Pandesal", ProductStage.Production, a)], isFiltered: false);

        TitlesIn(Root(tree, ProductStage.Production)).Should().Equal("A", "B");
        TitlesIn(Folder(tree, 10)).Should().Equal("Pandesal");
    }

    [Test]
    public void A_product_no_longer_listed_loses_its_row()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [ProductOf(1, "Pandesal", ProductStage.Production, Breads)], isFiltered: false);

        tree.Refresh(AllFolders, [], isFiltered: false);

        tree.Find(1).Should().BeNull();
        TitlesIn(Folder(tree, Breads.Id)).Should().Equal("Holiday");
    }

    [Test]
    public void MoveTo_a_folder_files_the_product_in_it_and_its_stage_and_its_category_stays()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.Innovation, Trials);
        mamon.CategoryId = 7;
        tree.Refresh(AllFolders, [mamon], isFiltered: false);

        ProductTree.MoveTo(Folder(tree, Holiday.Id), mamon).Should().Be(new ProductMove(1, ProductStage.Production, Holiday.Id));
        ProductTree.MoveTo(Root(tree, ProductStage.Production), mamon).Should().Be(new ProductMove(1, ProductStage.Production, null), "a stage's folder is its top");
        ProductTree.MoveTo(Root(tree, ProductStage.Innovation), mamon).Should().Be(new ProductMove(1, ProductStage.Innovation, null));
    }

    [Test]
    public void MoveTo_another_product_files_it_in_that_products_folder()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.Innovation, Trials);
        var sansRival = ProductOf(2, "Sans Rival", ProductStage.Production, Cakes);
        tree.Refresh(AllFolders, [mamon, sansRival], isFiltered: false);

        ProductTree.MoveTo(tree.Find(2), mamon).Should().Be(new ProductMove(1, ProductStage.Production, Cakes.Id));
    }

    [Test]
    public void MoveTo_where_the_product_is_already_or_nowhere_is_no_move()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.Innovation, Trials);
        var ensaymada = ProductOf(2, "Ensaymada", ProductStage.Innovation, Trials);
        var loose = ProductOf(3, "Loose", ProductStage.Innovation);
        tree.Refresh(AllFolders, [mamon, ensaymada, loose], isFiltered: false);

        ProductTree.MoveTo(Folder(tree, Trials.Id), mamon).Should().BeNull();
        ProductTree.MoveTo(tree.Find(2), mamon).Should().BeNull();
        ProductTree.MoveTo(Root(tree, ProductStage.Innovation), loose).Should().BeNull();
        ProductTree.MoveTo(null, mamon).Should().BeNull();
    }

    [Test]
    public void Each_folder_counts_the_products_in_it_and_in_the_folders_in_it_and_each_stage_all_of_its_own()
    {
        var tree = new ProductTree();
        Product[] products =
        [
            ProductOf(1, "Ensaymada", ProductStage.Production, Breads),
            ProductOf(2, "Bibingka", ProductStage.Production, Holiday),
            ProductOf(3, "Mamon", ProductStage.Production, Cakes),
            ProductOf(4, "Loose", ProductStage.Production),
            ProductOf(5, "Test Loaf", ProductStage.Innovation),
        ];

        tree.Refresh(AllFolders, products, isFiltered: false);

        Folder(tree, Breads.Id).Count.Should().Be(2);
        Folder(tree, Holiday.Id).Count.Should().Be(1);
        Folder(tree, Trials.Id).Count.Should().Be(0, "an empty folder says so");
        Root(tree, ProductStage.Production).Count.Should().Be(4);
        Root(tree, ProductStage.Innovation).Count.Should().Be(1);
        tree.Find(1)!.Count.Should().Be(0, "a product counts nothing");

        tree.Refresh(AllFolders, [products[1]], isFiltered: true);
        Folder(tree, Breads.Id).Count.Should().Be(1, "with a filter on, a folder counts its matches");
        Root(tree, ProductStage.Production).Count.Should().Be(1);
    }

    [Test]
    public void Expand_All_and_Collapse_All_reach_every_folder_in_the_one_given_however_deep()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [], isFiltered: false);
        tree.CollapseAll(null);
        var production = Root(tree, ProductStage.Production);
        production.IsExpanded = false;

        tree.ExpandAll(production);

        production.IsExpanded.Should().BeTrue();
        new[] { Breads.Id, Cakes.Id, Holiday.Id }.Select(id => Folder(tree, id)).Should().OnlyContain(f => f.IsExpanded);
        Folder(tree, Trials.Id).IsExpanded.Should().BeFalse("the other stage is left alone");

        tree.CollapseAll(Folder(tree, Breads.Id));
        Folder(tree, Holiday.Id).IsExpanded.Should().BeFalse();
        Folder(tree, Breads.Id).IsExpanded.Should().BeTrue("the folder given stays open on its folders");
        Folder(tree, Cakes.Id).IsExpanded.Should().BeTrue("one beside it is left alone");

        tree.ExpandAll(null);
        AllFolders.Select(f => Folder(tree, f.Id)).Should().OnlyContain(f => f.IsExpanded, "null is both stages");
        tree.DeletedFolder.IsExpanded.Should().BeTrue("and the Deleted folder");

        tree.CollapseAll(null);
        AllFolders.Select(f => Folder(tree, f.Id)).Should().OnlyContain(f => !f.IsExpanded);
        tree.Stages.Should().OnlyContain(r => r.IsExpanded, "the stages stay open on their folders");
        tree.DeletedFolder.IsExpanded.Should().BeFalse();
    }

    [Test]
    public void The_folders_left_closed_start_closed_and_the_rest_open_a_folder_new_since_included()
    {
        var first = new ProductTree();
        first.Refresh(AllFolders, [], isFiltered: false);
        Folder(first, Cakes.Id).IsExpanded = false;
        Folder(first, Holiday.Id).IsExpanded = false;
        Root(first, ProductStage.Innovation).IsExpanded = false;

        var closedFolders = first.ClosedFolderKeys();

        closedFolders.Should().Equal("Deleted", "Folder/2", "Folder/3", "Innovation");

        var next = new ProductTree(new FolderMemory(closedFolders));
        next.ClosedFolderKeys().Should().Equal(closedFolders, "before the first Refresh it keeps what it was given");
        ProductFolder pastries = new() { Id = 5, Name = "Pastries", Stage = ProductStage.Production };
        next.Refresh([.. AllFolders, pastries], [], isFiltered: false);
        Root(next, ProductStage.Production).IsExpanded.Should().BeTrue();
        Root(next, ProductStage.Innovation).IsExpanded.Should().BeFalse("a stage left closed starts closed");
        Folder(next, Cakes.Id).IsExpanded.Should().BeFalse("a folder made on the first Refresh starts as it was left");
        Folder(next, Holiday.Id).IsExpanded.Should().BeFalse();
        Folder(next, Breads.Id).IsExpanded.Should().BeTrue();
        Folder(next, pastries.Id).IsExpanded.Should().BeTrue("a folder new since opens");
        next.DeletedFolder.IsExpanded.Should().BeFalse();
        next.ClosedFolderKeys().Should().Equal(closedFolders);
    }

    [Test]
    public void A_stage_kept_under_innovations_former_name_starts_as_it_was_left_and_the_old_category_keys_are_forgotten()
    {
        // As a build before the rename kept them, and before folders were the user's own.
        var store = new InMemoryViewStateStore(new Dictionary<string, IReadOnlyList<string>>
        {
            [ProductsList.ClosedFoldersKey] = ["Deleted", "ResearchAndDevelopment", "ResearchAndDevelopment/2", "Production/Uncategorized"],
        });

        var tree = new ProductTree(FolderMemory.Read(
            store, ProductsList.ClosedFoldersKey, ProductsList.OpenFoldersKey, ProductFolderNode.CurrentFolderKey));
        tree.Refresh(AllFolders, [], isFiltered: false);

        Root(tree, ProductStage.Innovation).IsExpanded.Should().BeFalse();
        Root(tree, ProductStage.Production).IsExpanded.Should().BeTrue();
        AllFolders.Select(f => Folder(tree, f.Id)).Should().OnlyContain(f => f.IsExpanded, "no folder of the user's was kept yet");
        tree.ClosedFolderKeys().Should().Equal("Deleted", "Innovation");
    }

    [Test]
    public void An_empty_Refresh_as_the_screen_makes_its_tree_keeps_what_was_kept()
    {
        // The screen refreshes its tree, empty, as it's made, and a screen made but never loaded is disposed with the app.
        var closedKept = new ProductTree(new FolderMemory(["Deleted", $"Folder/{Breads.Id}"]));
        closedKept.Refresh([], [], isFiltered: false);
        closedKept.ClosedFolderKeys().Should().Equal("Deleted", $"Folder/{Breads.Id}");

        var openKept = new ProductTree(new FolderMemory(openKeys: ["Production", $"Folder/{Breads.Id}"]));
        openKept.Refresh([], [], isFiltered: false);
        openKept.ClosedFolderKeys().Should().BeEmpty("which folders are closed isn't known until the folders come");

        openKept.Refresh(AllFolders, [], isFiltered: false);
        openKept.ClosedFolderKeys().Should().Contain($"Folder/{Cakes.Id}").And.NotContain($"Folder/{Breads.Id}");
    }

    [Test]
    public void A_folder_opened_or_closed_is_reported()
    {
        var tree = new ProductTree();
        tree.Refresh([Breads], [], isFiltered: false);
        var reports = 0;
        using var subscription = tree.ExpansionChanged.Subscribe(_ => reports++);

        Folder(tree, Breads.Id).IsExpanded = false;
        Root(tree, ProductStage.Innovation).IsExpanded = false;
        tree.Refresh(AllFolders, [], isFiltered: false);
        Folder(tree, Cakes.Id).IsExpanded = false;

        reports.Should().Be(3, "a folder made since reports too");
    }

    [Test]
    public void MovesTo_several_products_leaves_out_the_ones_there_already()
    {
        var tree = new ProductTree();
        var mamon = ProductOf(1, "Mamon", ProductStage.Innovation, Trials);
        var pandesal = ProductOf(2, "Pandesal", ProductStage.Production, Breads);
        var sansRival = ProductOf(3, "Sans Rival", ProductStage.Innovation);
        tree.Refresh(AllFolders, [mamon, pandesal, sansRival], isFiltered: false);

        ProductTree.MovesTo(Folder(tree, Breads.Id), [mamon, pandesal, sansRival])
            .Should().Equal(new ProductMove(1, ProductStage.Production, Breads.Id), new ProductMove(3, ProductStage.Production, Breads.Id));

        ProductTree.MovesTo(ProductStage.Innovation, null, [mamon, sansRival])
            .Should().Equal([new ProductMove(1, ProductStage.Innovation, null)], "Sans Rival is at the top of Innovation already");
        ProductTree.MovesTo(ProductStage.Production, Breads.Id, [pandesal]).Should().BeEmpty();
    }

    [Test]
    public void A_folder_dropped_goes_into_a_folder_onto_a_stage_or_beside_a_product_but_never_inside_itself()
    {
        var tree = new ProductTree();
        var bibingka = ProductOf(1, "Bibingka", ProductStage.Production, Holiday);
        var mamon = ProductOf(2, "Mamon", ProductStage.Production, Cakes);
        var test = ProductOf(3, "Test", ProductStage.Innovation);
        tree.Refresh(AllFolders, [bibingka, mamon, test], isFiltered: false);
        var breads = Folder(tree, Breads.Id);
        var holiday = Folder(tree, Holiday.Id);

        ProductTree.FolderMoveTo(Folder(tree, Cakes.Id), holiday).Should().Be(new ProductFolderMove(Holiday.Id, ProductStage.Production, Cakes.Id));
        ProductTree.FolderMoveTo(Root(tree, ProductStage.Innovation), holiday).Should().Be(new ProductFolderMove(Holiday.Id, ProductStage.Innovation, null));
        ProductTree.FolderMoveTo(tree.Find(2), holiday).Should().Be(new ProductFolderMove(Holiday.Id, ProductStage.Production, Cakes.Id), "beside the product, in its folder");
        ProductTree.FolderMoveTo(tree.Find(3), holiday).Should().Be(new ProductFolderMove(Holiday.Id, ProductStage.Innovation, null));

        ProductTree.FolderMoveTo(breads, holiday).Should().BeNull("it's in Breads already");
        ProductTree.FolderMoveTo(holiday, holiday).Should().BeNull();
        ProductTree.FolderMoveTo(holiday, breads).Should().BeNull("a folder can't go inside a folder of its own");
        ProductTree.FolderMoveTo(tree.Find(1), breads).Should().BeNull("nor beside a product inside it");
        ProductTree.FolderMoveTo(tree.DeletedFolder, holiday).Should().BeNull();
        ProductTree.FolderMoveTo(Root(tree, ProductStage.Production), Root(tree, ProductStage.Innovation)).Should().BeNull("a stage isn't moved");
    }

    [Test]
    public void DropFor_moves_products_or_one_folder_and_nothing_mixed()
    {
        var tree = new ProductTree();
        var bibingka = ProductOf(1, "Bibingka", ProductStage.Production, Holiday);
        var mamon = ProductOf(2, "Mamon", ProductStage.Production, Cakes);
        tree.Refresh(AllFolders, [bibingka, mamon], isFiltered: false);
        var cakes = Folder(tree, Cakes.Id);
        var holiday = Folder(tree, Holiday.Id);

        ProductTree.DropFor([tree.Find(1)!, tree.Find(2)!], cakes)
            .Should().BeOfType<TreeDrop.Products>().Which.Moves.Should().Equal(new ProductMove(1, ProductStage.Production, Cakes.Id));
        ProductTree.DropFor([holiday], cakes).Should().Be(new TreeDrop.Folder(new ProductFolderMove(Holiday.Id, ProductStage.Production, Cakes.Id)));
        ProductTree.DropFor([holiday, tree.Find(2)!], cakes).Should().BeNull("products and a folder together go nowhere");
        ProductTree.DropFor([holiday, Folder(tree, Breads.Id)], cakes).Should().BeNull("nor several folders");
        ProductTree.DropFor([tree.Find(2)!], cakes).Should().BeNull("Mamon is there already");
        ProductTree.DropFor([], cakes).Should().BeNull();
    }

    [Test]
    public void PathTitle_names_where_a_folder_is()
    {
        var tree = new ProductTree();
        tree.Refresh(AllFolders, [], isFiltered: false);

        ProductTree.PathTitle(Folder(tree, Holiday.Id)).Should().Be("Production › Breads › Holiday");
        ProductTree.PathTitle(Root(tree, ProductStage.Innovation)).Should().Be("Innovation");
    }

    [Test]
    public void A_new_folder_sits_after_the_folders_and_before_the_products_of_its_folder_until_it_ends_and_a_refresh_keeps_it()
    {
        var tree = new ProductTree();
        Product[] products = [ProductOf(1, "Pandesal", ProductStage.Production, Breads)];
        tree.Refresh(AllFolders, products, isFiltered: false);
        tree.CollapseAll(null);
        var breads = Folder(tree, Breads.Id);
        var production = Root(tree, ProductStage.Production);
        production.IsExpanded = false;

        var folder = tree.BeginNewFolder(ProductStage.Production, Breads.Id);

        tree.PendingFolder.Should().BeSameAs(folder);
        folder.Should().Match<ProductFolderNode>(f => f.IsNewFolder && !f.IsUserFolder && f.Stage == ProductStage.Production && f.Title == "New folder");
        folder.ParentFolderId.Should().Be(Breads.Id);
        folder.Key.Should().Be("Folder/New");
        TitlesIn(breads).Should().Equal("Holiday", "New folder", "Pandesal");
        breads.IsExpanded.Should().BeTrue("the folders it's in open to show it");
        production.IsExpanded.Should().BeTrue();

        tree.Refresh(AllFolders, products, isFiltered: false);
        TitlesIn(breads).Should().Equal("Holiday", "New folder", "Pandesal");

        ProductTree.MoveTo(folder, products[0]).Should().BeNull("nothing is dropped on a folder that isn't saved yet");

        tree.BeginNewFolder(ProductStage.Innovation, null);
        TitlesIn(breads).Should().NotContain("New folder", "there's one at a time");
        TitlesIn(Root(tree, ProductStage.Innovation)).Should().Equal("Trials", "New folder");

        tree.EndNewFolder();
        tree.PendingFolder.Should().BeNull();
        TitlesIn(Root(tree, ProductStage.Innovation)).Should().Equal("Trials");
        tree.ClosedFolderKeys().Should().NotContain(k => k.EndsWith("/New"));
    }

    [Test]
    public void A_new_product_sits_last_in_its_folder_uncounted_until_it_ends_and_a_refresh_keeps_it()
    {
        var tree = new ProductTree();
        var pandesal = ProductOf(1, "Pandesal", ProductStage.Production, Breads);
        tree.Refresh(AllFolders, [pandesal], isFiltered: false);
        var breads = Folder(tree, Breads.Id);
        breads.IsExpanded = false;

        var row = tree.BeginNewProduct(ProductStage.Production, Breads.Id, isComposite: true);

        tree.PendingProduct.Should().BeSameAs(row);
        row.IsNew.Should().BeTrue();
        row.Product.Should().Match<Product>(p => p.Id == 0 && p.Name == "New composite product" && p.FolderId == Breads.Id && p.CategoryId == null && p.IsComposite);
        TitlesIn(breads).Should().Equal("Holiday", "Pandesal", "New composite product");
        breads.IsExpanded.Should().BeTrue("its folder opens to show it");

        tree.Refresh(AllFolders, [pandesal], isFiltered: false);
        TitlesIn(breads).Should().Equal("Holiday", "Pandesal", "New composite product");
        breads.Count.Should().Be(1, "it isn't a product until it's saved");
        Root(tree, ProductStage.Production).Count.Should().Be(1);
        tree.Find(0).Should().BeNull();
        ProductTree.MoveTo(row, ProductOf(2, "Mamon", ProductStage.Innovation)).Should().BeNull("nothing is dropped on it");
        ProductTree.MoveTo(breads, row.Product).Should().BeNull("and it isn't moved");

        tree.EndNewProduct();
        tree.PendingProduct.Should().BeNull();
        TitlesIn(breads).Should().Equal("Holiday", "Pandesal");

        tree.BeginNewProduct(ProductStage.Innovation, folderId: 99, isComposite: false);
        TitlesIn(Root(tree, ProductStage.Innovation)).Should().Equal(["Trials", "New product"], "a folder the list lacks files it at the top of the stage");
    }
}
