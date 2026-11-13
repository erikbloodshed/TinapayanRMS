using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

/// <summary>The Products list's folders: their names, where each sits, and the order the list shows and prints products in.</summary>
public class ProductFolderRulesTests
{
    // Production › Breads › Holiday › Christmas, Production › Cakes, Innovation › Trials.
    private static readonly ProductFolder[] Folders =
    [
        new() { Id = 1, Name = "Breads", Stage = ProductStage.Production },
        new() { Id = 2, Name = "Cakes", Stage = ProductStage.Production },
        new() { Id = 3, Name = "Holiday", Stage = ProductStage.Production, ParentId = 1 },
        new() { Id = 4, Name = "Christmas", Stage = ProductStage.Production, ParentId = 3 },
        new() { Id = 5, Name = "Trials", Stage = ProductStage.Innovation },
    ];

    [Test]
    [Arguments("Breads", "Breads")]
    [Arguments("  Holiday Breads ", "Holiday Breads")]
    public void A_name_is_stored_without_spaces_at_either_end(string name, string expected)
    {
        ProductFolderRules.StoredName(name).Should().Be(expected);
    }

    [Test]
    public void A_name_empty_or_too_long_is_refused()
    {
        var empty = () => ProductFolderRules.StoredName("   ");
        var tooLong = () => ProductFolderRules.StoredName(new string('a', ProductFolderRules.MaxNameLength + 1));

        empty.Should().Throw<BusinessRuleException>().WithMessage("Every folder needs a name.");
        tooLong.Should().Throw<BusinessRuleException>().WithMessage("A folder's name can be at most 100 characters.");
        ProductFolderRules.StoredName(new string('a', ProductFolderRules.MaxNameLength)).Should().HaveLength(ProductFolderRules.MaxNameLength);
    }

    [Test]
    public void SubtreeIds_is_the_folder_and_every_folder_in_it_however_deep()
    {
        ProductFolderRules.SubtreeIds(Folders, 1).Should().BeEquivalentTo([1, 3, 4]);
        ProductFolderRules.SubtreeIds(Folders, 4).Should().BeEquivalentTo([4]);
    }

    [Test]
    public void A_loop_of_folders_is_walked_once()
    {
        ProductFolder[] loop =
        [
            new() { Id = 1, Name = "A", ParentId = 2 },
            new() { Id = 2, Name = "B", ParentId = 1 },
        ];

        ProductFolderRules.SubtreeIds(loop, 1).Should().BeEquivalentTo([1, 2]);
        ProductFolderRules.IsInside(loop, 1, 99).Should().BeFalse();
        ProductFolderRules.PathOf(loop, 1).Should().Equal("B", "A");
    }

    [Test]
    public void IsInside_says_whether_a_folder_is_another_or_inside_it()
    {
        ProductFolderRules.IsInside(Folders, 4, 1).Should().BeTrue();
        ProductFolderRules.IsInside(Folders, 1, 1).Should().BeTrue();
        ProductFolderRules.IsInside(Folders, 1, 4).Should().BeFalse();
        ProductFolderRules.IsInside(Folders, 2, 1).Should().BeFalse();
    }

    [Test]
    public void EnsureNotInsideItself_refuses_a_folder_put_in_itself_or_one_of_its_own()
    {
        var intoItsOwn = () => ProductFolderRules.EnsureNotInsideItself(Folders, 1, 4);
        var intoItself = () => ProductFolderRules.EnsureNotInsideItself(Folders, 1, 1);

        intoItsOwn.Should().Throw<BusinessRuleException>().WithMessage("A folder can't go inside itself.");
        intoItself.Should().Throw<BusinessRuleException>();
        ProductFolderRules.EnsureNotInsideItself(Folders, 4, 2);
        ProductFolderRules.EnsureNotInsideItself(Folders, 4, null);
    }

    [Test]
    public void PathOf_and_PathTitle_name_where_a_folder_is_from_the_top_of_its_stage()
    {
        ProductFolderRules.PathOf(Folders, 4).Should().Equal("Breads", "Holiday", "Christmas");
        ProductFolderRules.PathOf(Folders, null).Should().BeEmpty();
        ProductFolderRules.PathOf(Folders, 99).Should().BeEmpty();
        ProductFolderRules.PathTitle(Folders, ProductStage.Production, 4).Should().Be("Production › Breads › Holiday › Christmas");
        ProductFolderRules.PathTitle(Folders, ProductStage.Innovation, null).Should().Be("Innovation");
    }

    [Test]
    public void InListOrder_is_each_stage_top_down_a_folders_folders_before_its_products_all_by_name()
    {
        Product Of(int id, string name, ProductStage stage, int? folderId) => new() { Id = id, Name = name, Stage = stage, FolderId = folderId };
        Product[] products =
        [
            Of(1, "Pandesal", ProductStage.Production, 1),
            Of(2, "Bibingka", ProductStage.Production, 3),
            Of(3, "Fruitcake", ProductStage.Production, 4),
            Of(4, "Mamon", ProductStage.Production, 2),
            Of(5, "Apple Pie", ProductStage.Production, null),
            Of(6, "Test Bun", ProductStage.Innovation, 5),
            Of(7, "Loose Bun", ProductStage.Innovation, null),
            Of(8, "Ensaymada", ProductStage.Production, 1),
            Of(9, "Lost", ProductStage.Innovation, 99),
        ];

        ProductFolderRules.InListOrder(products, Folders).Select(p => p.Name).Should().Equal(
            "Fruitcake", "Bibingka", "Ensaymada", "Pandesal", "Mamon", "Apple Pie", "Test Bun", "Loose Bun", "Lost");
    }

    [Test]
    public void InListOrder_lists_the_products_of_folders_whose_parents_come_round_in_a_loop_at_the_top_of_their_stage()
    {
        // A and B each other's parent, left by an edit outside the app, and C in A: the list shows all three at the top of
        // Production, as their parents never reach it (ProductTree).
        ProductFolder[] folders =
        [
            new() { Id = 1, Name = "A", Stage = ProductStage.Production, ParentId = 2 },
            new() { Id = 2, Name = "B", Stage = ProductStage.Production, ParentId = 1 },
            new() { Id = 3, Name = "C", Stage = ProductStage.Production, ParentId = 1 },
            new() { Id = 4, Name = "Breads", Stage = ProductStage.Production },
        ];
        Product Of(int id, string name, int? folderId) => new() { Id = id, Name = name, Stage = ProductStage.Production, FolderId = folderId };
        Product[] products = [Of(1, "In A", 1), Of(2, "In B", 2), Of(3, "In C", 3), Of(4, "In Breads", 4), Of(5, "Loose", null)];

        ProductFolderRules.IsInLoop(folders.ToDictionary(f => f.Id), 1).Should().BeTrue();
        ProductFolderRules.IsInLoop(folders.ToDictionary(f => f.Id), 3).Should().BeTrue("going up from C comes round A and B");
        ProductFolderRules.IsInLoop(folders.ToDictionary(f => f.Id), 4).Should().BeFalse();
        ProductFolderRules.InListOrder(products, folders).Select(p => p.Name).Should().Equal(
            ["In A", "In B", "In Breads", "In C", "Loose"], "every product is listed once, each looped folder at the top by name");
    }
}
