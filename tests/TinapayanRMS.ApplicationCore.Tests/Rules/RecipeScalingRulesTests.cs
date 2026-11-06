using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class RecipeScalingRulesTests
{
    [Test]
    public void PiecesFor_counts_the_pieces_in_the_sellable_units()
    {
        RecipeScalingRules.PiecesFor(20m, 8).Should().Be(160m);
        RecipeScalingRules.PiecesFor(20m, 1).Should().Be(20m);
    }

    [Test]
    public void BatchesFor_rounds_up_to_whole_batches()
    {
        // Ensaymada: 10 g a piece of a 1,000 g dough, a yield of 100. 160 pieces take 1.6 batches, so 2.
        RecipeScalingRules.BatchesFor(160m, UsageAuthoredField.WeightPerPiece, 10m, 1000m).Should().Be(2m);
    }

    [Test]
    public void BatchesFor_takes_an_exact_fit_as_it_is()
    {
        RecipeScalingRules.BatchesFor(200m, UsageAuthoredField.WeightPerPiece, 10m, 1000m).Should().Be(2m);
    }

    [Test]
    public void BatchesFor_works_from_the_typed_weight_so_a_worked_out_yield_adds_no_batch()
    {
        // 1,700 g at 60 g a piece yields 28.333…; 85 pieces are exactly 3 batches, and a yield rounded off would read 4.
        RecipeScalingRules.BatchesFor(85m, UsageAuthoredField.WeightPerPiece, 60m, 1700m).Should().Be(3m);
    }

    [Test]
    public void BatchesFor_divides_by_a_typed_yield()
    {
        RecipeScalingRules.BatchesFor(50m, UsageAuthoredField.Yield, 24m, 1500m).Should().Be(3m);
        RecipeScalingRules.BatchesFor(48m, UsageAuthoredField.Yield, 24m, 1500m).Should().Be(2m);
    }

    [Test]
    [Arguments(0, 1000)]
    [Arguments(10, 0)]
    public void BatchesFor_is_0_for_a_section_with_no_piece_size_or_no_batch(int authoredValue, int finalWeight)
    {
        RecipeScalingRules.BatchesFor(160m, UsageAuthoredField.WeightPerPiece, authoredValue, finalWeight).Should().Be(0m);
    }

    [Test]
    public void ExtraPieces_is_what_whole_batches_make_beyond_the_need()
    {
        RecipeScalingRules.ExtraPieces(2m, 100m, 160m).Should().Be(40m);
        RecipeScalingRules.ExtraPieces(2m, 100m, 200m).Should().Be(0m);
    }

    [Test]
    public void ScaleBom_scales_the_lines_to_make_the_grams_after_weight_loss()
    {
        // Syrup: 1,000 g sugar and 500 g water at 20% loss make 1,200 g. 600 g of it takes half.
        var scaled = RecipeScalingRules.ScaleBom(600m, [1000m, 500m], 20m);

        scaled.Should().Equal(500m, 250m);
    }

    [Test]
    public void ScaleBom_scales_a_bom_that_makes_nothing_to_nothing()
    {
        RecipeScalingRules.ScaleBom(600m, [0m], 0m).Should().Equal(0m);
    }

    [Test]
    public void BatchRatio_is_the_batches_before_rounding_and_WholeBatches_rounds_them_up()
    {
        RecipeScalingRules.BatchRatio(160m, UsageAuthoredField.WeightPerPiece, 10m, 1000m).Should().Be(1.6m);
        RecipeScalingRules.BatchRatio(50m, UsageAuthoredField.Yield, 24m, 1500m).Should().BeApproximately(2.0833m, 0.0001m);
        RecipeScalingRules.BatchRatio(50m, UsageAuthoredField.Yield, 0m, 1500m).Should().Be(0m);
        RecipeScalingRules.WholeBatches(1.6m).Should().Be(2m);
        RecipeScalingRules.WholeBatches(3.00001m).Should().Be(3m, "a hair over a whole batch is decimal noise");
        RecipeScalingRules.WholeBatches(0.00001m).Should().Be(1m, "but a need too small to survive that is still a batch");
        RecipeScalingRules.WholeBatches(0m).Should().Be(0m);
    }
}
