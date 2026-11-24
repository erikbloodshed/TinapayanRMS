using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing.Tests;

/// <summary>
/// A product's sections on each other's lines (SectionUses): a sponge on the dough's lines costs the dough its grams at the
/// sponge's cost per gram, and the sponge's own pieces come of what's left of its batch, so the product counts each gram once.
/// </summary>
public class SectionUsesTests
{
    // Flour at 0.05 a gram (0.06 buffered) and sugar at 0.10 (0.12 buffered).
    private static readonly RawMaterial Flour = Ingredient(1, "Flour", 0.05m, 0.06m);
    private static readonly RawMaterial Sugar = Ingredient(2, "Sugar", 0.10m, 0.12m);

    // The dough: 1,000 g of flour and 200 g of sugar, 1,200 g at 70.00 (84.00 buffered), cut 85 g a piece.
    private static PricedSection Dough(decimal spongeGrams = 500m) =>
        Section(10, "Dough", [new(Flour, 1000m), new(Sugar, 200m)], UsageAuthoredField.WeightPerPiece, 85m) with
        {
            SectionLines = spongeGrams > 0 ? [new SectionUse(20, spongeGrams, null, 2)] : [],
        };

    // The sponge: 400 g of flour and 100 g of sugar, 500 g at 30.00 (36.00 buffered): 0.06 a gram (0.072 buffered). Unlocked
    // unless said, so it can have pieces of what the dough leaves.
    private static PricedSection Sponge(UsageAuthoredField authoredField = UsageAuthoredField.WeightPerPiece, decimal authoredValue = 0m, bool unlocked = true) =>
        Section(20, "Sponge", [new(Flour, 400m), new(Sugar, 100m)], authoredField, authoredValue) with { IsUnlocked = unlocked };

    [Test]
    public void The_dough_weighs_and_costs_its_lines_and_its_grams_of_sponge_and_its_piece_shares_out_both()
    {
        var dough = SectionUses.Apply([Dough(), Sponge()])[0];

        dough.TotalWeight.Should().Be(1700m);
        dough.Figures.BaseTotalWeight.Should().Be(1200m, "its own lines are still its own");
        dough.Figures.SectionLinesWeight.Should().Be(500m);
        dough.TotalCostAt(CostBasis.Unbuffered).Should().Be(100m);
        dough.TotalCostAt(CostBasis.Buffered).Should().Be(120m);
        dough.Yield.Should().Be(20m, "1,700 g at 85 g a piece");
        dough.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(5m);
        dough.CostPerPieceAt(CostBasis.Buffered).Should().Be(6m);
        dough.SectionLines.Should().ContainSingle().Which.Should().Match<SectionUse>(l =>
            l.SectionName == "Sponge" && l.CostPerGramAt(CostBasis.Unbuffered) == 0.06m && l.CostPerGramAt(CostBasis.Buffered) == 0.072m && l.CostAt(CostBasis.Unbuffered) == 30m);
    }

    [Test]
    public void A_part_of_the_sponge_costs_its_grams()
    {
        var dough = SectionUses.Apply([Dough(spongeGrams: 250m), Sponge()])[0];

        dough.TotalWeight.Should().Be(1450m);
        dough.TotalCostAt(CostBasis.Unbuffered).Should().Be(85m, "70.00 of its own and 250 g at 0.06");
        dough.TotalCostAt(CostBasis.Buffered).Should().Be(102m);
    }

    [Test]
    public void The_sponges_yield_shares_out_what_the_dough_leaves()
    {
        var sponge = SectionUses.Apply([Dough(spongeGrams: 300m), Sponge(UsageAuthoredField.Yield, 10m)])[1];

        sponge.TotalWeight.Should().Be(500m, "its batch is still its batch");
        sponge.Figures.UsedElsewhereWeight.Should().Be(300m);
        sponge.IsUsedElsewhere.Should().BeTrue();
        (sponge.WeightPerPiece, sponge.Yield).Should().Be((20m, 10m), "the 200 g left, in 10 pieces");
        sponge.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(1.2m);
        sponge.CostPerPieceAt(CostBasis.Buffered).Should().Be(1.44m);
        sponge.LacksPieceSize.Should().BeFalse();
    }

    [Test]
    public void A_weight_per_piece_costs_its_weight_and_counts_its_pieces_of_what_is_left()
    {
        var sponge = SectionUses.Apply([Dough(spongeGrams: 300m), Sponge(UsageAuthoredField.WeightPerPiece, 25m)])[1];

        (sponge.WeightPerPiece, sponge.Yield).Should().Be((25m, 8m), "200 g left at 25 g a piece");
        sponge.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(1.5m);
    }

    [Test]
    public void A_sponge_with_no_piece_size_that_the_dough_uses_lacks_nothing()
    {
        var sponge = SectionUses.Apply([Dough(), Sponge()])[1];

        sponge.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m, "the dough counts it");
        sponge.LacksPieceSize.Should().BeFalse();
        Sponge().LacksPieceSize.Should().BeTrue("one nothing uses, with no piece size, costs the product nothing it should");
    }

    [Test]
    public void A_yield_with_nothing_left_has_no_pieces_and_lacks_a_piece_size()
    {
        var sponge = SectionUses.Apply([Dough(spongeGrams: 600m), Sponge(UsageAuthoredField.Yield, 10m)])[1];

        (sponge.WeightPerPiece, sponge.CostPerPieceAt(CostBasis.Buffered)).Should().Be((0m, 0m));
        sponge.LacksPieceSize.Should().BeTrue();
    }

    [Test]
    public void A_weight_per_piece_with_nothing_left_cuts_no_pieces_so_nothing_is_counted_twice()
    {
        var sponge = SectionUses.Apply([Dough(spongeGrams: 500m), Sponge(UsageAuthoredField.WeightPerPiece, 50m)])[1];

        (sponge.WeightPerPiece, sponge.Yield, sponge.CostPerPieceAt(CostBasis.Buffered)).Should().Be((0m, 0m, 0m), "the dough takes the whole batch");
        sponge.LacksPieceSize.Should().BeTrue();
    }

    [Test]
    public void A_locked_sponge_cuts_no_pieces_whatever_its_piece_size_and_lacks_nothing()
    {
        var sponge = SectionUses.Apply([Dough(spongeGrams: 300m), Sponge(UsageAuthoredField.Yield, 10m, unlocked: false)])[1];

        sponge.IsLocked.Should().BeTrue();
        (sponge.WeightPerPiece, sponge.CostPerPieceAt(CostBasis.Buffered)).Should().Be((0m, 0m), "it's only the dough's ingredient");
        sponge.LacksPieceSize.Should().BeFalse();
        Sponge(UsageAuthoredField.Yield, 10m, unlocked: false).IsLocked.Should().BeFalse("nothing uses it, so nothing locks it");
    }

    [Test]
    public void A_chain_prices_through()
    {
        // The starter, 100 g of flour, 5.00 (6.00 buffered), whole on the sponge's lines; the sponge whole on the dough's.
        var starter = Section(30, "Starter", [new(Flour, 100m)], UsageAuthoredField.WeightPerPiece, 0m);
        var sponge = Sponge() with { SectionLines = [new SectionUse(30, 100m, null, 2)] };

        var priced = SectionUses.Apply([Dough(spongeGrams: 600m), sponge, starter]);

        priced[1].TotalWeight.Should().Be(600m);
        priced[1].TotalCostAt(CostBasis.Unbuffered).Should().Be(35m);
        priced[0].TotalWeight.Should().Be(1800m);
        priced[0].TotalCostAt(CostBasis.Unbuffered).Should().Be(105m, "600 g of a sponge at 35.00 for 600 g");
    }

    [Test]
    public void Sections_of_ingredients_alone_come_back_as_they_were()
    {
        IReadOnlyList<PricedSection> sections = [Dough(spongeGrams: 0m), Sponge()];

        SectionUses.Apply(sections).Should().BeSameAs(sections);
    }

    [Test]
    public void A_loop_is_cut_where_it_closes_so_nothing_is_counted_twice()
    {
        // Never saved (EnsureValidSectionLines), but a reader of a broken row doesn't run for ever.
        var sponge = Sponge() with { SectionLines = [new SectionUse(10, 100m, null, 2)] };

        var priced = SectionUses.Apply([Dough(), sponge]);

        // The dough takes the sponge in; the sponge, reached from the dough, takes nothing back.
        priced.Select(s => s.TotalWeight).Should().Equal(1700m, 500m);
    }

    private static PricedSection Section(int subAssemblyId, string name, IReadOnlyList<SectionLine> lines, UsageAuthoredField authoredField, decimal authoredValue) =>
        new(
            subAssemblyId,
            null,
            null,
            name,
            null,
            null,
            authoredField,
            authoredValue,
            lines,
            [],
            SectionFigures.Of(lines, [], authoredField, authoredValue));

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
