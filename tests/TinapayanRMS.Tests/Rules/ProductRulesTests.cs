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
    public void EnsureValidUsages_accepts_a_section_that_has_no_piece_size_yet()
    {
        // 0 is "not sized yet": a section goes into the recipe before how much of it a piece takes is known.
        var act = () => ProductRules.EnsureValidUsages([(SubAssemblyId: 1, AuthoredValue: 0m, ShrinkagePercent: 0m)]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidUsages_rejects_a_negative_piece_size()
    {
        var act = () => ProductRules.EnsureValidUsages([(SubAssemblyId: 1, AuthoredValue: -1m, ShrinkagePercent: 0m)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*can't be negative*");
    }

    [Fact]
    public void EnsureValidUsages_rejects_the_same_sub_assembly_twice()
    {
        var act = () => ProductRules.EnsureValidUsages(
        [
            (SubAssemblyId: 1, AuthoredValue: 10m, ShrinkagePercent: 0m),
            (SubAssemblyId: 1, AuthoredValue: 20m, ShrinkagePercent: 0m),
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidUsages_accepts_distinct_positive_usages()
    {
        var act = () => ProductRules.EnsureValidUsages(
        [
            (SubAssemblyId: 1, AuthoredValue: 10m, ShrinkagePercent: 12m),
            (SubAssemblyId: 2, AuthoredValue: 20m, ShrinkagePercent: 0m),
        ]);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(100)]
    [InlineData(150)]
    public void EnsureValidUsages_rejects_a_shrinkage_outside_0_to_under_100(double shrinkagePercent)
    {
        // 100% or more would leave nothing to cut pieces from, and a negative would make a batch heavier than
        // what went into it.
        var act = () => ProductRules.EnsureValidUsages(
            [(SubAssemblyId: 1, AuthoredValue: 60m, ShrinkagePercent: (decimal)shrinkagePercent)]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*shrinkage*");
    }

    [Fact]
    public void CalculateFinalWeight_takes_the_shrinkage_off_the_batch()
    {
        // A 1,700 g dough batch that loses a tenth of itself baking comes out at 1,530 g.
        ProductRules.CalculateFinalWeight(totalWeight: 1700m, shrinkagePercent: 10m).Should().Be(1530m);
    }

    [Fact]
    public void CalculateFinalWeight_leaves_a_batch_that_loses_nothing_alone()
    {
        ProductRules.CalculateFinalWeight(totalWeight: 1700m, shrinkagePercent: 0m).Should().Be(1700m);
    }

    [Fact]
    public void Shrinkage_cuts_the_yield_and_leaves_the_typed_weight_per_piece_alone()
    {
        // 1,700 g of dough at 10% comes out at 1,530 g. A 60 g finished piece is 25.5 of them, not 28.33.
        var finalWeight = ProductRules.CalculateFinalWeight(totalWeight: 1700m, shrinkagePercent: 10m);

        ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 60m, finalWeight).Should().Be((60m, 25.5m));
    }

    [Fact]
    public void Shrinkage_makes_each_piece_heavier_when_the_yield_is_the_typed_figure()
    {
        // The same 1,530 g cut into a typed 24 pieces: each finished piece weighs 63.75 g.
        var finalWeight = ProductRules.CalculateFinalWeight(totalWeight: 1700m, shrinkagePercent: 10m);

        ProductRules.ResolvePieceSize(UsageAuthoredField.Yield, 24m, finalWeight).Should().Be((63.75m, 24m));
    }

    [Fact]
    public void Shrinkage_makes_every_piece_dearer_because_the_batch_still_costs_the_same()
    {
        // A 100 g filling batch costing 24 keeps its cost when it reduces to 80 g, so a 10 g piece of what's
        // left carries 3.00 rather than 2.40 — and the batch's cost over its yield comes to the same thing.
        var finalWeight = ProductRules.CalculateFinalWeight(totalWeight: 100m, shrinkagePercent: 20m);
        var (weightPerPiece, yield) = ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 10m, finalWeight);

        var costPerPiece = ProductRules.CalculateUsageCostPerPiece(subAssemblyTotalCost: 24m, weightPerPiece, finalWeight);

        costPerPiece.Should().Be(3m);
        costPerPiece.Should().Be(24m / yield);
    }

    [Fact]
    public void ResolvePieceSize_works_out_the_yield_from_a_typed_weight_per_piece()
    {
        // 100g filling batch, used at 10g/piece -> 10 pieces.
        ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 10m, finalWeight: 100m).Should().Be((10m, 10m));
    }

    [Fact]
    public void ResolvePieceSize_works_out_the_weight_per_piece_from_a_typed_yield()
    {
        // A 1,200 g dough batch that makes 24 pieces -> 50 g a piece.
        ProductRules.ResolvePieceSize(UsageAuthoredField.Yield, 24m, finalWeight: 1200m).Should().Be((50m, 24m));
    }

    [Fact]
    public void When_the_batch_changes_the_typed_figure_stays_put_and_the_other_follows()
    {
        // The batch grows from 1,200 g to 1,500 g.
        ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 50m, finalWeight: 1500m)
            .Should().Be((50m, 30m), "a typed 50 g piece stays 50 g, so the batch makes more pieces");
        ProductRules.ResolvePieceSize(UsageAuthoredField.Yield, 24m, finalWeight: 1500m)
            .Should().Be((62.5m, 24m), "a typed 24 pieces stays 24, so each piece gets heavier");
    }

    [Fact]
    public void ResolvePieceSize_has_no_other_figure_for_a_batch_that_weighs_nothing_yet()
    {
        ProductRules.ResolvePieceSize(UsageAuthoredField.WeightPerPiece, 10m, finalWeight: 0m).Should().Be((10m, 0m));
        ProductRules.ResolvePieceSize(UsageAuthoredField.Yield, 24m, finalWeight: 0m).Should().Be((0m, 24m));
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
        ProductRules.CalculateUsageCostPerPiece(subAssemblyTotalCost: 24m, weightPerPiece: 10m, finalWeight: 100m)
            .Should().Be(2.4m);
    }

    [Fact]
    public void CalculateLineGramsPerPiece_takes_the_pieces_share_of_the_line()
    {
        // A 1,200 g dough batch used at 50 g a piece: each piece takes 1/24 of every line.
        // 300 g of flour in the batch is 12.5 g in a piece.
        ProductRules.CalculateLineGramsPerPiece(lineGrams: 300m, weightPerPiece: 50m, finalWeight: 1200m)
            .Should().Be(12.5m);
    }

    [Fact]
    public void CalculateLineCostPerPiece_takes_the_pieces_share_of_the_lines_cost()
    {
        // 300 g of flour at ₱0.05 a gram costs ₱15 in the batch; a 50 g piece of a 1,200 g batch takes ₱0.625.
        ProductRules.CalculateLineCostPerPiece(lineGrams: 300m, ingredientCostPerGram: 0.05m, weightPerPiece: 50m, finalWeight: 1200m)
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
    public void EnsureValidPackagingLines_rejects_a_non_positive_quantity()
    {
        var act = () => ProductRules.EnsureValidPackagingLines([(PackagingMaterialId: 1, QtyPerUnit: 0m)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidPackagingLines_rejects_the_same_material_twice()
    {
        // One line per material: a material a unit takes twice over belongs on a single line, at the
        // combined quantity. There's no second place for it to be since packs went.
        var act = () => ProductRules.EnsureValidPackagingLines(
        [
            (PackagingMaterialId: 1, QtyPerUnit: 5m),
            (PackagingMaterialId: 1, QtyPerUnit: 3m),
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidPackagingLines_accepts_different_materials()
    {
        var act = () => ProductRules.EnsureValidPackagingLines(
        [
            (PackagingMaterialId: 1, QtyPerUnit: 1m),
            (PackagingMaterialId: 2, QtyPerUnit: 8m),
        ]);

        act.Should().NotThrow();
    }

    [Fact]
    public void ReconcileReciprocal_derives_QtyPerUnit_from_an_authored_UnitsPerPackagingUnit()
    {
        // A box: 6 pieces per box means 0.1667 (1/6) of the box per piece.
        var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(PackagingLineAuthoredField.UnitsPerPackagingUnit, 6m);

        unitsPerPackagingUnit.Should().Be(6m);
        qtyPerUnit.Should().Be(1m / 6m);
    }

    [Fact]
    public void ReconcileReciprocal_derives_UnitsPerPackagingUnit_from_an_authored_QtyPerUnit()
    {
        // A ribbon: 12cm per piece means 1/12 of a "packaging unit of ribbon" per piece.
        var (qtyPerUnit, unitsPerPackagingUnit) = ProductRules.ReconcileReciprocal(PackagingLineAuthoredField.QtyPerUnit, 12m);

        qtyPerUnit.Should().Be(12m);
        unitsPerPackagingUnit.Should().Be(1m / 12m);
    }


    [Fact]
    public void ReconcileReciprocal_rejects_an_undefined_authored_field()
    {
        var act = () => ProductRules.ReconcileReciprocal((PackagingLineAuthoredField)999, 6m);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void CalculatePackagingLineCost_matches_the_ribbon_worked_example()
    {
        // A ribbon costs ₱0.045/cm (₱45 per 1000cm roll), used at 12.7cm per piece -> ₱0.5715.
        var costPerPiece = ProductRules.CalculatePackagingLineCost(
            costPerBaseUnit: 0.045m, qtyPerUnit: 12.7m, baseUnitsPerEntryUnit: null);

        costPerPiece.Should().Be(0.5715m);
    }

    [Fact]
    public void CalculatePackagingLineCost_converts_a_line_entered_in_an_alternative_unit()
    {
        // The same ribbon entered in metres (100 cm each): 0.127 m per piece -> 12.7 cm -> ₱0.5715.
        var costPerPiece = ProductRules.CalculatePackagingLineCost(
            costPerBaseUnit: 0.045m, qtyPerUnit: 0.127m, baseUnitsPerEntryUnit: 100m);

        costPerPiece.Should().Be(0.5715m);
    }

    [Fact]
    public void CalculatePackagingLineCost_matches_the_box_worked_example()
    {
        // A box costs ₱0.30/piece (₱30 per 100-piece pack), used one per finished piece.
        var costPerPiece = ProductRules.CalculatePackagingLineCost(
            costPerBaseUnit: 0.3m, qtyPerUnit: 1m, baseUnitsPerEntryUnit: null);

        costPerPiece.Should().Be(0.3m);
    }

    [Fact]
    public void CalculateMaterialCost_sums_usage_and_packaging_costs_for_a_product_sold_by_the_piece()
    {
        ProductRules.CalculateMaterialCost([2.4m, 1.1m], 1, [0.5715m, 0.3m]).Should().Be(4.3715m);
    }

    [Fact]
    public void CalculateMaterialCost_multiplies_only_the_recipe_by_the_quantity_required()
    {
        // A Box of 8: 8 pieces at ₱4.50 of recipe each is ₱36, and the box they go in is ₱12 on top —
        // once, not eight times, because a packaging line is already for one whole sellable unit.
        ProductRules.CalculateMaterialCost([4.50m], 8, [12m]).Should().Be(48m);
    }

    [Fact]
    public void CalculateMaterialCost_of_a_unit_with_no_packaging_is_just_its_pieces()
    {
        ProductRules.CalculateMaterialCost([4.50m], 8, []).Should().Be(36m);
    }

    [Fact]
    public void CalculateEffectiveBatch_adds_the_add_ins_to_the_sub_assemblys_batch()
    {
        // 1,700 g of dough costing ₱85, plus 20 g of ube flavouring @ ₱0.80/g (₱16).
        var (totalWeight, totalCost) = ProductRules.CalculateEffectiveBatch(
            subAssemblyTotalWeight: 1700m,
            subAssemblyTotalCost: 85m,
            [(Grams: 20m, IngredientCostPerGram: 0.8m)]);

        totalWeight.Should().Be(1720m);
        totalCost.Should().Be(101m);
    }

    [Fact]
    public void CalculateEffectiveBatch_with_no_add_ins_is_the_sub_assemblys_own_batch()
    {
        ProductRules.CalculateEffectiveBatch(1700m, 85m, []).Should().Be((1700m, 85m));
    }

    [Fact]
    public void EnsureValidAddIns_refuses_an_ingredient_the_linked_recipe_already_has()
    {
        var act = () => ProductRules.EnsureValidAddIns(
            [(IngredientId: 7, Grams: 10m, IngredientName: "Butter")],
            "Sweet Dough",
            [7, 9]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Butter*").WithMessage("*Sweet Dough*");
    }

    [Fact]
    public void EnsureValidAddIns_refuses_the_same_ingredient_on_two_add_in_lines()
    {
        var act = () => ProductRules.EnsureValidAddIns(
            [
                (IngredientId: 7, Grams: 10m, IngredientName: "Ube Flavour"),
                (IngredientId: 7, Grams: 5m, IngredientName: "Ube Flavour"),
            ],
            "Sweet Dough",
            []);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Ube Flavour*");
    }

    [Fact]
    public void EnsureValidAddIns_refuses_grams_that_arent_positive()
    {
        var act = () => ProductRules.EnsureValidAddIns(
            [(IngredientId: 7, Grams: 0m, IngredientName: "Ube Flavour")],
            "Sweet Dough",
            []);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidAddIns_allows_a_usage_with_no_add_ins()
    {
        // Unlike a sub-assembly's own lines: most linked usages take the recipe as it is.
        var act = () => ProductRules.EnsureValidAddIns([], "Sweet Dough", [7, 9]);

        act.Should().NotThrow();
    }
}
