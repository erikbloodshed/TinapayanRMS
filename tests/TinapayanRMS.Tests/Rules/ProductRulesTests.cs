using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.Tests.Rules;

public class ProductRulesTests
{
    [Fact]
    public void EnsureValidUsages_accepts_an_empty_list()
    {
        // A product doesn't have to have a sub-assembly at all times: a new one starts empty.
        var act = () => ProductRules.EnsureValidUsages([]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidUsages_rejects_a_non_positive_piece_size()
    {
        var act = () => ProductRules.EnsureValidUsages([(SubAssemblyId: 1, AuthoredValue: 0m)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidUsages_rejects_the_same_sub_assembly_twice()
    {
        var act = () => ProductRules.EnsureValidUsages(
        [
            (SubAssemblyId: 1, AuthoredValue: 10m),
            (SubAssemblyId: 1, AuthoredValue: 20m),
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidUsages_accepts_distinct_positive_usages()
    {
        var act = () => ProductRules.EnsureValidUsages(
        [
            (SubAssemblyId: 1, AuthoredValue: 10m),
            (SubAssemblyId: 2, AuthoredValue: 20m),
        ]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ResolvePieceSize_works_out_the_yield_from_a_typed_weight_per_piece()
    {
        // 100g filling batch, used at 10g/piece -> 10 pieces.
        ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 10m, totalWeight: 100m).Should().Be((10m, 10m));
    }

    [Fact]
    public void ResolvePieceSize_works_out_the_weight_per_piece_from_a_typed_yield()
    {
        // A 1,200 g dough batch that makes 24 pieces -> 50 g a piece.
        ProductRules.ResolvePieceSize(UsageAuthoredField.Yield, 24m, totalWeight: 1200m).Should().Be((50m, 24m));
    }

    [Fact]
    public void When_the_batch_changes_the_typed_figure_stays_put_and_the_other_follows()
    {
        // The batch grows from 1,200 g to 1,500 g.
        ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 50m, totalWeight: 1500m)
            .Should().Be((50m, 30m), "a typed 50 g piece stays 50 g, so the batch makes more pieces");
        ProductRules.ResolvePieceSize(UsageAuthoredField.Yield, 24m, totalWeight: 1500m)
            .Should().Be((62.5m, 24m), "a typed 24 pieces stays 24, so each piece gets heavier");
    }

    [Fact]
    public void ResolvePieceSize_has_no_other_figure_for_a_batch_that_weighs_nothing_yet()
    {
        ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 10m, totalWeight: 0m).Should().Be((10m, 0m));
        ProductRules.ResolvePieceSize(UsageAuthoredField.Yield, 24m, totalWeight: 0m).Should().Be((0m, 24m));
    }

    [Fact]
    public void ResolvePieceSize_rejects_an_undefined_authored_field()
    {
        var act = () => ProductRules.ResolvePieceSize((UsageAuthoredField)999, 10m, 100m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculateUsageCostPerPiece_matches_the_worked_example()
    {
        // 100g/₱24 filling used at 10g/piece -> ₱2.40/piece.
        ProductRules.CalculateUsageCostPerPiece(subAssemblyTotalCost: 24m, weightPerPiece: 10m, subAssemblyTotalWeight: 100m)
            .Should().Be(2.4m);
    }

    [Fact]
    public void CalculateLineGramsPerPiece_takes_the_pieces_share_of_the_line()
    {
        // A 1,200 g dough batch used at 50 g a piece: each piece takes 1/24 of every line.
        // 300 g of flour in the batch is 12.5 g in a piece.
        ProductRules.CalculateLineGramsPerPiece(lineGrams: 300m, weightPerPiece: 50m, subAssemblyTotalWeight: 1200m)
            .Should().Be(12.5m);
    }

    [Fact]
    public void CalculateLineCostPerPiece_takes_the_pieces_share_of_the_lines_cost()
    {
        // 300 g of flour at ₱0.05 a gram costs ₱15 in the batch; a 50 g piece of a 1,200 g batch takes ₱0.625.
        ProductRules.CalculateLineCostPerPiece(lineGrams: 300m, ingredientCostPerGram: 0.05m, weightPerPiece: 50m, subAssemblyTotalWeight: 1200m)
            .Should().Be(0.625m);
    }

    [Fact]
    public void A_usages_line_costs_per_piece_add_up_to_its_cost_per_piece()
    {
        // Custard: 60 g of sugar at ₱0.09/g and 40 g of milk at ₱0.10/g, a 100 g batch costing ₱9.40,
        // used at 25 g a piece.
        (decimal Grams, decimal CostPerGram)[] lines = [(60m, 0.09m), (40m, 0.10m)];
        var (totalWeight, totalCost) = SubAssemblyRules.CalculateTotals(lines);

        var sumOfLines = lines.Sum(l => ProductRules.CalculateLineCostPerPiece(l.Grams, l.CostPerGram, weightPerPiece: 25m, totalWeight));

        sumOfLines.Should().Be(ProductRules.CalculateUsageCostPerPiece(totalCost, weightPerPiece: 25m, totalWeight));
        sumOfLines.Should().Be(2.35m);
    }

    [Fact]
    public void EnsureValidPackagingLines_accepts_an_empty_list()
    {
        // Unlike EnsureValidUsages, an empty packaging line list is a valid state — a
        // product may not have its packaging entered yet.
        var act = () => ProductRules.EnsureValidPackagingLines([]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidPackagingLines_rejects_non_positive_qty_per_piece()
    {
        var act = () => ProductRules.EnsureValidPackagingLines([(PackagingMaterialId: 1, QtyPerPiece: 0m)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidPackagingLines_rejects_the_same_material_twice()
    {
        var act = () => ProductRules.EnsureValidPackagingLines(
        [
            (PackagingMaterialId: 1, QtyPerPiece: 5m),
            (PackagingMaterialId: 1, QtyPerPiece: 3m),
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ReconcileReciprocal_derives_PiecesPerPackagingUnit_from_an_authored_QtyPerPiece()
    {
        // A box: 6 pieces per box means 0.1667 (1/6) of the box per piece.
        var (qtyPerPiece, piecesPerPackagingUnit) = ProductRules.ReconcileReciprocal(PackagingLineAuthoredField.PiecesPerPackagingUnit, 6m);

        piecesPerPackagingUnit.Should().Be(6m);
        qtyPerPiece.Should().Be(1m / 6m);
    }

    [Fact]
    public void ReconcileReciprocal_derives_QtyPerPiece_from_an_authored_PiecesPerPackagingUnit()
    {
        // A ribbon: 12cm per piece means 1/12 of a "packaging unit of ribbon" per piece.
        var (qtyPerPiece, piecesPerPackagingUnit) = ProductRules.ReconcileReciprocal(PackagingLineAuthoredField.QtyPerPiece, 12m);

        qtyPerPiece.Should().Be(12m);
        piecesPerPackagingUnit.Should().Be(1m / 12m);
    }

    [Fact]
    public void ReconcileReciprocal_rejects_an_undefined_authored_field()
    {
        var act = () => ProductRules.ReconcileReciprocal((PackagingLineAuthoredField)999, 6m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculatePackagingLineCostPerPiece_matches_the_ribbon_worked_example()
    {
        // A ribbon costs ₱0.045/cm (₱45 per 1000cm roll), used at 12.7cm per piece -> ₱0.5715.
        var costPerPiece = ProductRules.CalculatePackagingLineCostPerPiece(
            costPerBaseUnit: 0.045m, qtyPerPiece: 12.7m, baseUnitsPerEntryUnit: null);

        costPerPiece.Should().Be(0.5715m);
    }

    [Fact]
    public void CalculatePackagingLineCostPerPiece_converts_a_line_entered_in_an_alternative_unit()
    {
        // The same ribbon entered in metres (100 cm each): 0.127 m per piece -> 12.7 cm -> ₱0.5715.
        var costPerPiece = ProductRules.CalculatePackagingLineCostPerPiece(
            costPerBaseUnit: 0.045m, qtyPerPiece: 0.127m, baseUnitsPerEntryUnit: 100m);

        costPerPiece.Should().Be(0.5715m);
    }

    [Fact]
    public void CalculatePackagingLineCostPerPiece_matches_the_box_worked_example()
    {
        // A box costs ₱0.30/piece (₱30 per 100-piece pack), used one per finished piece.
        var costPerPiece = ProductRules.CalculatePackagingLineCostPerPiece(
            costPerBaseUnit: 0.3m, qtyPerPiece: 1m, baseUnitsPerEntryUnit: null);

        costPerPiece.Should().Be(0.3m);
    }

    [Fact]
    public void CalculateMaterialCostPerPiece_sums_usage_and_packaging_costs()
    {
        ProductRules.CalculateMaterialCostPerPiece([2.4m, 1.1m], [0.5715m, 0.3m]).Should().Be(4.3715m);
    }
}
