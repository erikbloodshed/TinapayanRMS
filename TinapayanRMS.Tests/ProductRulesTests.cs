using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests;

public class ProductRulesTests
{
    [Fact]
    public void EnsureValidUsages_accepts_an_empty_list()
    {
        // Usages are set one role (tab) at a time (Phase 10) — an empty list just means
        // this particular role currently has no usages, not that the whole product does.
        var act = () => ProductRules.EnsureValidUsages([]);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidUsages_rejects_non_positive_weight_per_piece()
    {
        var act = () => ProductRules.EnsureValidUsages([(SubAssemblyId: 1, WeightPerPiece: 0m)]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidUsages_rejects_the_same_sub_assembly_twice()
    {
        var act = () => ProductRules.EnsureValidUsages(
        [
            (SubAssemblyId: 1, WeightPerPiece: 10m),
            (SubAssemblyId: 1, WeightPerPiece: 20m),
        ]);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidUsages_accepts_distinct_positive_usages()
    {
        var act = () => ProductRules.EnsureValidUsages(
        [
            (SubAssemblyId: 1, WeightPerPiece: 10m),
            (SubAssemblyId: 2, WeightPerPiece: 20m),
        ]);

        act.Should().NotThrow();
    }

    [Fact]
    public void CalculateYield_divides_total_weight_by_weight_per_piece()
    {
        // 100g filling batch, used at 10g/piece -> 10 pieces.
        ProductRules.CalculateYield(subAssemblyTotalWeight: 100m, weightPerPiece: 10m).Should().Be(10m);
    }

    [Fact]
    public void CalculateUsageCostPerPiece_matches_the_worked_example()
    {
        // 100g/₱24 filling used at 10g/piece -> ₱2.40/piece.
        ProductRules.CalculateUsageCostPerPiece(subAssemblyTotalCost: 24m, weightPerPiece: 10m, subAssemblyTotalWeight: 100m)
            .Should().Be(2.4m);
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
    public void EnsureValidEntryUnit_accepts_the_materials_own_standard_unit()
    {
        var act = () => ProductRules.EnsureValidEntryUnit(WellKnownUnitOfMeasureIds.Piece, WellKnownUnitOfMeasureIds.Piece);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidEntryUnit_accepts_any_length_unit_when_the_standard_unit_is_a_length_unit()
    {
        var act = () => ProductRules.EnsureValidEntryUnit(WellKnownUnitOfMeasureIds.Centimeter, WellKnownUnitOfMeasureIds.Inch);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureValidEntryUnit_rejects_a_length_unit_for_a_piece_standard_unit()
    {
        var act = () => ProductRules.EnsureValidEntryUnit(WellKnownUnitOfMeasureIds.Piece, WellKnownUnitOfMeasureIds.Centimeter);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidEntryUnit_rejects_piece_for_a_length_standard_unit()
    {
        var act = () => ProductRules.EnsureValidEntryUnit(WellKnownUnitOfMeasureIds.Centimeter, WellKnownUnitOfMeasureIds.Piece);

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
    public void CalculatePackagingLineCostPerPiece_matches_the_worked_example_with_no_unit_conversion()
    {
        // A ribbon costs ₱45 per 1000cm roll (₱0.045/cm). 5 inches per piece = 12.7cm -> ₱0.5715.
        var costPerPiece = ProductRules.CalculatePackagingLineCostPerPiece(
            packagingMaterialCurrentCost: 45m, purchaseUnitQty: 1000m, qtyPerPiece: 5m,
            entryUnitId: WellKnownUnitOfMeasureIds.Inch, standardUnitId: WellKnownUnitOfMeasureIds.Centimeter);

        costPerPiece.Should().Be(0.5715m);
    }

    [Fact]
    public void CalculatePackagingLineCostPerPiece_needs_no_conversion_when_entry_unit_matches_standard_unit()
    {
        // A box costs ₱30 per 100-piece pack (₱0.30/piece), used one per finished piece.
        var costPerPiece = ProductRules.CalculatePackagingLineCostPerPiece(
            packagingMaterialCurrentCost: 30m, purchaseUnitQty: 100m, qtyPerPiece: 1m,
            entryUnitId: WellKnownUnitOfMeasureIds.Piece, standardUnitId: WellKnownUnitOfMeasureIds.Piece);

        costPerPiece.Should().Be(0.3m);
    }

    [Fact]
    public void CalculateMaterialCostPerPiece_sums_usage_and_packaging_costs()
    {
        ProductRules.CalculateMaterialCostPerPiece([2.4m, 1.1m], [0.5715m, 0.3m]).Should().Be(4.3715m);
    }
}
