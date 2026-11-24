using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

/// <summary>
/// A version's sections on each other's lines (VersionSection.SectionLines, schema 5): listed as changes by the section
/// used, kept through the JSON, and a schema 4 snapshot's section that went whole into another read as a line of its whole
/// batch on that one's.
/// </summary>
public class ProductVersionSectionLineTests
{
    private const int Flour = 1;
    private const int Water = 2;
    private static readonly VersionRole Dough = new(1, "Dough", 1, false, true);
    private static readonly VersionRole Sponge = new(2, "Sponge", 2, false, true);

    private static ProductVersionContent SpanishBread(decimal? spongeGrams, int doughId = 100, int spongeId = 200, bool? spongeUnlocked = null) => new()
    {
        ProductName = "Spanish Bread",
        QuantityRequired = 1,
        Sections =
        [
            new VersionSection(doughId, "Spanish Bread Dough", true, Dough, UsageAuthoredField.WeightPerPiece, 70m, [new VersionLine(Flour, 1000m, true, null)], [],
                SectionLines: spongeGrams is { } grams ? [new VersionSectionLine(spongeId, grams, null, 1)] : null),
            new VersionSection(spongeId, "Spanish Bread Sponge", true, Sponge, UsageAuthoredField.WeightPerPiece, 0m, [new VersionLine(Flour, 400m, false, null)], [],
                IsUnlocked: spongeUnlocked),
        ],
        Materials = [new VersionMaterial(Flour, "Bread Flour", RawMaterialType.Ingredient, null, 0.05m, 0.05m, [])],
    };

    [Test]
    public void A_section_line_added_changed_or_removed_is_listed()
    {
        var without = SpanishBread(spongeGrams: null);
        var with = SpanishBread(spongeGrams: 400m);

        ProductVersionRules.Changes(without, with).Should().Equal("Dough: Sponge added, 400 g");
        ProductVersionRules.Changes(with, SpanishBread(spongeGrams: 250m)).Should().Equal("Dough: Sponge 400 g → 250 g");
        ProductVersionRules.Changes(with, without).Should().Equal("Dough: Sponge removed");
        ProductVersionRules.IsSameRecipe(without, with).Should().BeFalse("a Save that only adds a section's line records a version");
    }

    [Test]
    public void Unlocking_a_used_section_or_locking_it_again_is_listed_and_kept()
    {
        var locked = SpanishBread(spongeGrams: 400m);
        var unlocked = SpanishBread(spongeGrams: 400m, spongeUnlocked: true);

        ProductVersionRules.Changes(locked, unlocked).Should().Equal("Sponge: unlocked");
        ProductVersionRules.Changes(unlocked, locked).Should().Equal("Sponge: locked as an ingredient again");
        ProductVersionContent.FromJson(unlocked.ToJson()).Sections[1].IsUnlocked.Should().BeTrue();
        locked.ToJson().Should().NotContain("IsUnlocked", "a locked one is left out, as an older snapshot has it");
    }

    [Test]
    public void Sections_rewritten_under_new_sub_assemblies_are_the_same_lines()
    {
        // A restored version saves the product's own sections afresh: both come back under new sub-assemblies.
        ProductVersionRules.Changes(SpanishBread(spongeGrams: 400m), SpanishBread(spongeGrams: 400m, doughId: 300, spongeId: 400)).Should().BeEmpty();
    }

    [Test]
    public void A_snapshot_keeps_its_section_lines_and_leaves_none_out()
    {
        var with = SpanishBread(spongeGrams: 400m);

        ProductVersionContent.FromJson(with.ToJson()).Sections[0].SectionLines.Should().Equal(new VersionSectionLine(200, 400m, null, 1));
        SpanishBread(spongeGrams: null).ToJson().Should().NotContain("SectionLines", "none is left out, as an older snapshot has it");
    }

    [Test]
    public void A_schema_4_section_that_went_whole_into_another_reads_as_a_line_of_its_whole_batch()
    {
        // As schema 4 wrote it: the sponge (400 g of flour, 250 g of water), with a starter going whole into it, went whole
        // into the dough, after the dough's one line.
        var json = (new ProductVersionContent
        {
            Schema = 4,
            ProductName = "Spanish Bread",
            Sections =
            [
                new VersionSection(100, "Spanish Bread Dough", true, Dough, UsageAuthoredField.WeightPerPiece, 70m, [new VersionLine(Flour, 1000m, true, null)], []),
                new VersionSection(200, "Spanish Bread Sponge", true, Sponge, UsageAuthoredField.WeightPerPiece, 0m,
                    [new VersionLine(Flour, 400m, false, null), new VersionLine(Water, 250m, false, null)], [], GoesIntoSubAssemblyId: 100),
                new VersionSection(300, "Spanish Bread Starter", true, Sponge, UsageAuthoredField.WeightPerPiece, 0m,
                    [new VersionLine(Flour, 30m, false, null)], [], Label: "Starter", GoesIntoSubAssemblyId: 200),
            ],
        }).ToJson();

        var read = ProductVersionContent.FromJson(json);

        read.Sections.Should().AllSatisfy(s => s.GoesIntoSubAssemblyId.Should().BeNull());
        read.Sections[0].SectionLines.Should().Equal(new VersionSectionLine(200, 680m, null, 1));
        read.Sections[1].SectionLines.Should().Equal(new VersionSectionLine(300, 30m, null, 2));
        read.Sections[2].SectionLines.Should().BeEmpty();
    }
}
