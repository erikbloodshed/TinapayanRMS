using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Pricing.Tests;

/// <summary>
/// A section's batch and piece figures at both bases (SectionFigures.Of), which the Products screen's sections and every
/// saved product's are worked out from, at the edges: a piece size typed either way, add-ins, none typed yet, an empty batch.
/// </summary>
public class SectionFiguresTests
{
    // Flour at 0.05 a gram (0.06 buffered) and sugar at 0.10 (0.12 buffered): 1,000 g and 200 g make a 1,200 g batch that
    // costs 70.00, or 84.00 buffered.
    private static readonly RawMaterial Flour = Ingredient(1, "Flour", 0.05m, 0.06m);
    private static readonly RawMaterial Sugar = Ingredient(2, "Sugar", 0.10m, 0.12m);
    private static readonly RawMaterial Butter = Ingredient(3, "Butter", 0.30m, 0.36m);

    private static readonly IReadOnlyList<SectionLine> Batch = [new(Flour, 1000m), new(Sugar, 200m)];

    [Test]
    public void A_typed_weight_per_piece_cuts_the_batch_into_its_yield_and_shares_its_cost_out_by_weight()
    {
        var figures = SectionFigures.Of(Batch, [], UsageAuthoredField.WeightPerPiece, 60m);

        figures.BaseTotalWeight.Should().Be(1200m);
        figures.TotalWeight.Should().Be(1200m);
        figures.WeightPerPiece.Should().Be(60m);
        figures.Yield.Should().Be(20m);
        figures.BufferedTotalCost.Should().Be(84m);
        figures.UnbufferedTotalCost.Should().Be(70m);
        figures.CostPerPieceAt(CostBasis.Buffered).Should().Be(4.2m);
        figures.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(3.5m);
    }

    [Test]
    public void A_typed_yield_works_out_the_weight_per_piece_and_keeps_the_cost_per_piece_at_full_precision()
    {
        var figures = SectionFigures.Of(Batch, [], UsageAuthoredField.Yield, 24m);

        figures.WeightPerPiece.Should().Be(50m);
        figures.Yield.Should().Be(24m);
        figures.CostPerPieceAt(CostBasis.Buffered).Should().Be(3.5m);
        figures.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(2.9166666666666666666666666667m, "70 × 50 ÷ 1,200, never rounded");
    }

    [Test]
    public void Add_ins_go_into_the_batch_so_a_piece_takes_its_share_of_them_too()
    {
        // 100 g of butter on top: a 1,300 g batch at 100.00, or 120.00 buffered, cut 65 g a piece.
        var figures = SectionFigures.Of(Batch, [new(Butter, 100m)], UsageAuthoredField.WeightPerPiece, 65m);

        figures.BaseTotalWeight.Should().Be(1200m);
        figures.TotalWeight.Should().Be(1300m);
        figures.Yield.Should().Be(20m);
        figures.BufferedTotalCost.Should().Be(120m);
        figures.UnbufferedTotalCost.Should().Be(100m);
        figures.CostPerPieceAt(CostBasis.Buffered).Should().Be(6m);
        figures.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(5m);
    }

    [Test]
    [Arguments(UsageAuthoredField.WeightPerPiece)]
    [Arguments(UsageAuthoredField.Yield)]
    public void A_section_with_no_piece_size_yet_costs_nothing_per_piece_though_its_batch_still_costs(UsageAuthoredField authoredField)
    {
        var figures = SectionFigures.Of(Batch, [], authoredField, 0m);

        figures.WeightPerPiece.Should().Be(0m);
        figures.Yield.Should().Be(0m);
        figures.BufferedTotalCost.Should().Be(84m);
        figures.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m);
        figures.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(0m);
    }

    [Test]
    [Arguments(UsageAuthoredField.WeightPerPiece)]
    [Arguments(UsageAuthoredField.Yield)]
    public void A_batch_that_weighs_nothing_costs_nothing_per_piece_whatever_piece_size_is_typed(UsageAuthoredField authoredField)
    {
        var figures = SectionFigures.Of([], [], authoredField, 10m);

        figures.TotalWeight.Should().Be(0m);
        figures.CostPerPieceAt(CostBasis.Buffered).Should().Be(0m);
        figures.CostPerPieceAt(CostBasis.Unbuffered).Should().Be(0m);
    }

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
