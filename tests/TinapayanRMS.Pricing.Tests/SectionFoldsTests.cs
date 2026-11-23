using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing.Tests;

/// <summary>
/// A product's sections folded into each other (SectionFolds): a sponge mixed whole into the dough is in the dough's batch,
/// which its piece size shares out, and has no pieces of its own, so the product counts it once.
/// </summary>
public class SectionFoldsTests
{
    // Flour at 0.05 a gram (0.06 buffered) and sugar at 0.10 (0.12 buffered).
    private static readonly RawMaterial Flour = Ingredient(1, "Flour", 0.05m, 0.06m);
    private static readonly RawMaterial Sugar = Ingredient(2, "Sugar", 0.10m, 0.12m);

    // The dough: 1,000 g of flour and 200 g of sugar, 1,200 g at 70.00 (84.00 buffered), cut 85 g a piece.
    private static PricedSection Dough() => Section(10, "Dough", [new(Flour, 1000m), new(Sugar, 200m)], 85m);

    // The sponge: 400 g of flour and 100 g of sugar, 500 g at 30.00 (36.00 buffered).
    private static PricedSection Sponge(int? goesInto = 10, decimal authoredValue = 0m) =>
        Section(20, "Sponge", [new(Flour, 400m), new(Sugar, 100m)], authoredValue) with { GoesIntoSubAssemblyId = goesInto };

    [Test]
    public void The_dough_weighs_and_costs_its_own_lines_and_the_whole_sponge_and_its_piece_shares_out_both()
    {
        var folded = SectionFolds.Apply([Dough(), Sponge()]);

        var dough = folded[0];
        dough.TotalWeight.Should().Be(1700m);
        dough.Figures.BaseTotalWeight.Should().Be(1200m, "its own lines are still its own");
        dough.Figures.FoldedInWeight.Should().Be(500m);
        dough.TotalCostAt(CostBasis.Unbuffered).Should().Be(100m);
        dough.TotalCostAt(CostBasis.Buffered).Should().Be(120m);
        dough.Yield.Should().Be(20m, "1,700 g at 85 g a piece");
        dough.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(5m);
        dough.CostPerPieceAt(CostBasis.Buffered).Should().Be(6m);
        dough.FoldedIn.Should().Equal(new FoldedSection("Sponge", new FoldedBatch(500m, 36m, 30m)));
        dough.GoesWhole.Should().BeFalse();
    }

    [Test]
    public void The_sponge_keeps_its_batch_but_has_no_pieces_of_its_own()
    {
        var sponge = SectionFolds.Apply([Dough(), Sponge()])[1];

        sponge.GoesWhole.Should().BeTrue();
        sponge.GoesIntoName.Should().Be("Dough");
        sponge.TotalWeight.Should().Be(500m);
        sponge.TotalCostAt(CostBasis.Unbuffered).Should().Be(30m);
        (sponge.WeightPerPiece, sponge.Yield).Should().Be((0m, 0m));
        sponge.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m, "the dough counts it");
    }

    [Test]
    public void A_piece_size_left_on_a_section_that_goes_into_another_still_cuts_no_pieces()
    {
        var sponge = SectionFolds.Apply([Dough(), Sponge(authoredValue: 25m)])[1];

        sponge.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m, "or the product would count the sponge twice");
    }

    [Test]
    public void A_chain_sums_through_into_the_last()
    {
        // 100 g of flour, 5.00 (6.00 buffered), into the sponge, into the dough.
        var starter = Section(30, "Starter", [new(Flour, 100m)], 0m) with { GoesIntoSubAssemblyId = 20 };

        var folded = SectionFolds.Apply([Dough(), Sponge(), starter]);

        folded[1].TotalWeight.Should().Be(600m);
        folded[1].TotalCostAt(CostBasis.Unbuffered).Should().Be(35m);
        folded[0].TotalWeight.Should().Be(1800m);
        folded[0].TotalCostAt(CostBasis.Unbuffered).Should().Be(105m);
        folded[0].FoldedIn.Should().ContainSingle().Which.Batch.Weight.Should().Be(600m, "the sponge with the starter in it");
    }

    [Test]
    public void Sections_that_go_into_nothing_come_back_as_they_were()
    {
        IReadOnlyList<PricedSection> sections = [Dough(), Sponge(goesInto: null)];

        SectionFolds.Apply(sections).Should().BeSameAs(sections);
    }

    [Test]
    public void A_loop_is_cut_where_it_closes_so_nothing_is_counted_twice()
    {
        // Never saved (EnsureValidFolds), but a reader of a broken row doesn't run for ever.
        var dough = Dough() with { GoesIntoSubAssemblyId = 20 };

        var folded = SectionFolds.Apply([dough, Sponge()]);

        // The dough takes the sponge in; the sponge, reached from the dough, takes nothing back.
        folded.Select(s => s.TotalWeight).Should().Equal(1700m, 500m);
    }

    private static PricedSection Section(int subAssemblyId, string name, IReadOnlyList<SectionLine> lines, decimal weightPerPiece) =>
        new(
            subAssemblyId,
            null,
            null,
            name,
            null,
            null,
            UsageAuthoredField.WeightPerPiece,
            weightPerPiece,
            lines,
            [],
            SectionFigures.Of(lines, [], UsageAuthoredField.WeightPerPiece, weightPerPiece));

    private static RawMaterial Ingredient(int id, string name, decimal cost, decimal bufferedCost) => new()
    {
        Id = id,
        Name = name,
        Type = RawMaterialType.Ingredient,
        Source = RawMaterialSource.Purchased,
        CostPerBaseUnit = cost,
        BufferedCostPerBaseUnit = bufferedCost,
    };
}
