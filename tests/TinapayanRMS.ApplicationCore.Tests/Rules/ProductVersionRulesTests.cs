using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class ProductVersionRulesTests
{
    private const int Flour = 1;
    private const int Sugar = 2;
    private const int Salt = 3;
    private const int Bag = 10;

    private static readonly VersionRole Dough = new(1, "Dough", 1, false, false);
    private static readonly VersionRole Filling = new(2, "Filling", 2, false, true);

    private static readonly IReadOnlyList<VersionMaterial> Materials =
    [
        new(Flour, "Bread Flour", RawMaterialType.Ingredient, null, 0.05m, 0.055m, []),
        new(Sugar, "Sugar", RawMaterialType.Ingredient, null, 0.08m, 0.088m, []),
        new(Salt, "Salt", RawMaterialType.Ingredient, null, 0.02m, 0.022m, []),
        new(Bag, "Paper Bag", RawMaterialType.Packaging, new VersionUnitOfMeasure(5, "piece", "pc"), 1.5m, 1.65m,
            [new VersionMaterialUnit(20, new VersionUnitOfMeasure(6, "pack", "pack"), 100m, null)]),
    ];

    private static VersionSection DoughSection(params VersionLine[] lines) =>
        new(100, "Pandesal Dough", false, Dough, UsageAuthoredField.WeightPerPiece, 60m, lines, []);

    private static ProductVersionContent Pandesal(params VersionSection[] sections) => new()
    {
        ProductName = "Pandesal",
        QuantityRequired = 1,
        CurrentPrice = 5m,
        Costing = new VersionCosting(25m, 10m, 5m, 30m, 12m),
        Sections = sections,
        PackagingLines = [new VersionPackagingLine(Bag, null, 1m)],
        Materials = Materials,
    };

    private static readonly ProductVersionContent Base = Pandesal(
        DoughSection(new VersionLine(Flour, 500m, true, null), new VersionLine(Sugar, 50m, false, null)));

    [Test]
    public void A_version_has_no_changes_from_itself()
    {
        ProductVersionRules.Changes(Base, Base).Should().BeEmpty();
        ProductVersionRules.IsSameRecipe(Base, Base).Should().BeTrue();
    }

    [Test]
    public void Materials_costs_name_and_line_order_are_not_changes()
    {
        var later = Base with
        {
            ProductName = "Pandesal Classic",
            Materials = [.. Materials.Select(m => m with { CostPerBaseUnit = m.CostPerBaseUnit * 2 })],
            Sections = [DoughSection(new VersionLine(Sugar, 50m, false, null), new VersionLine(Flour, 500m, true, null))],
        };

        ProductVersionRules.IsSameRecipe(Base, later).Should().BeTrue();
    }

    [Test]
    public void A_line_changed_added_and_removed_are_each_listed()
    {
        var later = Pandesal(DoughSection(new VersionLine(Flour, 450m, true, null), new VersionLine(Salt, 10m, false, null)));

        ProductVersionRules.Changes(Base, later).Should().Equal(
            "Dough: Bread Flour 500 g → 450 g",
            "Dough: Salt added, 10 g",
            "Dough: Sugar removed");
    }

    [Test]
    public void An_add_in_changed_added_and_removed_are_each_listed()
    {
        static ProductVersionContent WithAddIns(params VersionAddIn[] addIns) => Pandesal(
            new VersionSection(100, "Pandesal Dough", false, Dough, UsageAuthoredField.WeightPerPiece, 60m, [new VersionLine(Flour, 500m, true, null)], addIns));

        var before = WithAddIns(new VersionAddIn(Sugar, 20m));
        var later = WithAddIns(new VersionAddIn(Salt, 5m), new VersionAddIn(Sugar, 25m));

        ProductVersionRules.Changes(before, later).Should().Equal(
            "Dough: add-in Salt added, 5 g",
            "Dough: add-in Sugar 20 g → 25 g");
        ProductVersionRules.Changes(later, before).Should().Equal(
            "Dough: add-in Sugar 25 g → 20 g",
            "Dough: add-in Salt removed");
    }

    [Test]
    public void The_piece_size_is_listed()
    {
        var later = Base with
        {
            Sections = [Base.Sections[0] with { AuthoredValue = 55m }],
        };

        ProductVersionRules.Changes(Base, later).Should().Equal("Dough: weight per piece 60 g → 55 g");
    }

    [Test]
    public void The_shop_wide_wastage_isnt_part_of_a_versions_recipe()
    {
        // It's the shop's, set in Settings, like a material's price: a change of it alone records no version.
        ProductVersionRules.IsSameRecipe(Base with { WastagePercent = 3m }, Base with { WastagePercent = 5m }).Should().BeTrue();
    }

    [Test]
    public void A_section_rewritten_as_a_new_one_of_the_products_own_is_the_same_section()
    {
        var own = new VersionSection(7, "Pandesal Filling", true, Filling, UsageAuthoredField.Yield, 1m, [new VersionLine(Sugar, 30m, false, null)], []);
        var before = Base with { Sections = [.. Base.Sections, own] };
        var after = Base with { Sections = [.. Base.Sections, own with { SubAssemblyId = 9 }] };

        ProductVersionRules.IsSameRecipe(before, after).Should().BeTrue();
    }

    [Test]
    public void A_section_added_and_removed_are_listed_by_role()
    {
        var own = new VersionSection(7, "Pandesal Filling", true, Filling, UsageAuthoredField.Yield, 1m, [new VersionLine(Sugar, 30m, false, null)], []);
        var withFilling = Base with { Sections = [.. Base.Sections, own] };

        ProductVersionRules.Changes(Base, withFilling).Should().Equal("Filling added");
        ProductVersionRules.Changes(withFilling, Base).Should().Equal("Filling removed");
    }

    [Test]
    public void Price_quantity_packaging_and_costing_are_listed()
    {
        var later = Base with
        {
            CurrentPrice = 6m,
            QuantityRequired = 2,
            PackagingLines = [new VersionPackagingLine(Bag, 20, 0.02m)],
            Costing = Base.Costing! with { MarkupPercent = 35m },
        };

        ProductVersionRules.Changes(Base, later).Should().Equal(
            "Quantity Required: 1 → 2",
            "Current Price: 5.00 → 6.00",
            "Packaging: Paper Bag 1 pc → 0.02 pack",
            "Markup %: 30 → 35");
    }

    [Test]
    public void A_packaging_line_added_and_removed_are_each_listed()
    {
        var unpacked = Base with { PackagingLines = [] };

        ProductVersionRules.Changes(Base, unpacked).Should().Equal("Packaging: Paper Bag removed");
        ProductVersionRules.Changes(unpacked, Base).Should().Equal("Packaging: Paper Bag added, 1 pc");
    }

    [Test]
    public void Bulk_packaging_is_listed_apart_from_the_lines_with_how_many_products_a_pack_holds()
    {
        // The paper bag is both a line for each unit and the bulk pack's, and each is told apart.
        var bulked = Base with { PackagingLines = [.. Base.PackagingLines, new VersionPackagingLine(Bag, null, 2m, IsBulk: true)], BulkPackagingQuantity = 6 };
        var bulkedFor8 = bulked with { BulkPackagingQuantity = 8 };

        ProductVersionRules.Changes(Base, bulked).Should().Equal("Bulk packaging: Paper Bag added, 2 pc", "Bulk packaging: 6 products per pack");
        ProductVersionRules.Changes(bulked, bulkedFor8).Should().Equal("Bulk packaging: 6 → 8 products per pack");
        ProductVersionRules.Changes(bulked, Base).Should().Equal("Bulk packaging: Paper Bag removed");
    }

    [Test]
    public void A_packaging_link_is_listed_by_what_it_links()
    {
        var linked = Base with { PackagingLink = new VersionPackagingLink(null, 50, "Gift Box Set") };

        ProductVersionRules.Changes(Base, linked).Should().Equal("Packaging: now linked to Gift Box Set");
        ProductVersionRules.Changes(linked, Base).Should().Equal("Packaging: now the product's own (was linked to Gift Box Set)");
    }

    [Test]
    public void The_shops_vat_and_wastage_are_shop_changes_not_the_products()
    {
        // Base is a snapshot from before VAT was the shop's: its costing kept the product's own 12%.
        var later = Base with
        {
            Schema = ProductVersionContent.CurrentSchema,
            Costing = new VersionCosting(25m, 10m, 5m, 30m),
            VatPercent = 10m,
            WastagePercent = 5m,
        };

        ProductVersionRules.Changes(Base, later).Should().BeEmpty("neither is the product's");
        ProductVersionRules.IsSameRecipe(Base, later).Should().BeTrue("a Save records no version for the shop's figures alone");
        ProductVersionRules.ShopChanges(Base, later).Should().Equal("VAT % (Settings): 12 → 10", "Wastage % (Settings or its category): 0 → 5");
        ProductVersionRules.ShopChanges(later, later).Should().BeEmpty();
    }

    [Test]
    public void No_costing_saved_is_the_same_as_all_zero()
    {
        var none = Base with { Costing = null };
        var zero = Base with { Costing = new VersionCosting(0m, 0m, 0m, 0m, 0m) };

        ProductVersionRules.IsSameRecipe(none, zero).Should().BeTrue();
    }

    [Test]
    public void A_composites_products_are_listed_by_name()
    {
        var box = new ProductVersionContent
        {
            ProductName = "Pasalubong Box",
            IsComposite = true,
            Components = [new VersionComponent(1, "Ensaymada", 6, 20m, 18m, 1m, 1m), new VersionComponent(2, "Cheese Roll", 4, 10m, 9m, 1m, 1m)],
        };
        var later = box with
        {
            Components = [new VersionComponent(1, "Ensaymada", 8, 20m, 18m, 1m, 1m), new VersionComponent(3, "Ube Roll", 2, 12m, 11m, 1m, 1m)],
        };

        ProductVersionRules.Changes(box, later).Should().Equal(
            "Products: Ensaymada × 6 → × 8",
            "Products: Ube Roll added, × 2",
            "Products: Cheese Roll removed");
    }

    [Test]
    public void A_snapshot_reads_back_as_it_was_written()
    {
        var read = ProductVersionContent.FromJson(Base.ToJson());

        ProductVersionRules.IsSameRecipe(Base, read).Should().BeTrue();
        read.Materials.Should().HaveCount(Materials.Count);
        read.Materials.Single(m => m.Id == Bag).Units.Single().BaseUnitsPerUnit.Should().Be(100m);
        read.Sections.Single().AuthoredField.Should().Be(UsageAuthoredField.WeightPerPiece);
    }

    [Test]
    public void A_snapshot_keeps_the_markup_under_the_name_every_stored_one_has()
    {
        // MarkupPercent was MarginPercent until 2026-11-22: every snapshot in the database holds it by that name.
        var json = Base.ToJson();
        json.Should().Contain("\"MarginPercent\":30").And.NotContain("MarkupPercent");

        ProductVersionContent.FromJson(json).Costing!.MarkupPercent.Should().Be(30m);
    }

    [Test]
    public void A_snapshot_keeps_the_wastage_it_was_priced_with()
    {
        ProductVersionContent.FromJson((Base with { WastagePercent = 3m }).ToJson()).WastagePercent.Should().Be(3m);
    }

    [Test]
    public void A_snapshot_saved_with_shrinkage_before_there_was_wastage_still_reads()
    {
        // An older snapshot has a ShrinkagePercent on each section and no WastagePercent: the one is ignored, and
        // the other reads as none, so the version prices with no wastage.
        var json = Base.ToJson().Replace("\"AuthoredValue\":60", "\"AuthoredValue\":60,\"ShrinkagePercent\":3", StringComparison.Ordinal);
        json.Should().Contain("ShrinkagePercent");

        var read = ProductVersionContent.FromJson(json);

        ProductVersionRules.IsSameRecipe(Base, read).Should().BeTrue();
        read.WastagePercent.Should().BeNull();
    }

    [Test]
    public void A_snapshot_that_cant_be_read_is_refused()
    {
        var act = () => ProductVersionContent.FromJson("{ not json");

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    [Arguments(null, null)]
    [Arguments("   ", null)]
    [Arguments("  Cheaper butter ", "Cheaper butter")]
    public void A_note_is_trimmed_and_blank_is_none(string? note, string? stored)
    {
        ProductVersionRules.NormalizeNote(note).Should().Be(stored);
    }

    [Test]
    public void A_note_too_long_is_refused()
    {
        var act = () => ProductVersionRules.NormalizeNote(new string('a', ProductVersionRules.MaxNoteLength + 1));

        act.Should().Throw<BusinessRuleException>();
    }
}
