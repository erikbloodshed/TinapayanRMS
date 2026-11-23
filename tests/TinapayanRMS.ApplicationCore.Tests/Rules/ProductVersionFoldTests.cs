using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

/// <summary>
/// A version's sections going whole into each other (VersionSection.GoesIntoSubAssemblyId, schema 4): listed as a change by
/// the section it goes into, kept through the JSON, and read as none from a snapshot written before.
/// </summary>
public class ProductVersionFoldTests
{
    private const int Flour = 1;
    private static readonly VersionRole Dough = new(1, "Dough", 1, false, true);
    private static readonly VersionRole Sponge = new(2, "Sponge", 2, false, true);

    private static ProductVersionContent SpanishBread(int? spongeGoesInto, int doughId = 100) => new()
    {
        ProductName = "Spanish Bread",
        QuantityRequired = 1,
        Sections =
        [
            new VersionSection(doughId, "Spanish Bread Dough", true, Dough, UsageAuthoredField.WeightPerPiece, 70m, [new VersionLine(Flour, 1000m, true, null)], []),
            new VersionSection(200, "Spanish Bread Sponge", true, Sponge, UsageAuthoredField.WeightPerPiece, 0m, [new VersionLine(Flour, 400m, false, null)], [],
                GoesIntoSubAssemblyId: spongeGoesInto),
        ],
        Materials = [new VersionMaterial(Flour, "Bread Flour", RawMaterialType.Ingredient, null, 0.05m, 0.05m, [])],
    };

    [Test]
    public void A_section_going_into_another_or_no_longer_is_listed()
    {
        var apart = SpanishBread(spongeGoesInto: null);
        var folded = SpanishBread(spongeGoesInto: 100);

        ProductVersionRules.Changes(apart, folded).Should().Equal("Sponge: now goes whole into Dough");
        ProductVersionRules.Changes(folded, apart).Should().Equal("Sponge: no longer goes into Dough");
        ProductVersionRules.IsSameRecipe(apart, folded).Should().BeFalse("a Save that only folds a section in records a version");
    }

    [Test]
    public void The_dough_rewritten_under_a_new_sub_assembly_is_still_what_it_goes_into()
    {
        // A restored version saves the product's own sections afresh: the dough comes back as sub-assembly 300.
        ProductVersionRules.Changes(SpanishBread(spongeGoesInto: 100), SpanishBread(spongeGoesInto: 300, doughId: 300)).Should().BeEmpty();
    }

    [Test]
    public void A_snapshot_keeps_what_a_section_goes_into_and_an_older_one_reads_as_none()
    {
        var folded = SpanishBread(spongeGoesInto: 100);

        ProductVersionContent.FromJson(folded.ToJson()).Sections[1].GoesIntoSubAssemblyId.Should().Be(100);
        SpanishBread(spongeGoesInto: null).ToJson().Should().NotContain("GoesIntoSubAssemblyId", "none is left out, as an older snapshot has it");
    }
}
