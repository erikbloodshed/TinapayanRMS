using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

/// <summary>
/// The row behind every packaging table: a quantity typed in the material's base unit or in one of its alternative
/// units, which is what the line stores, and the base-unit figure and cost that follow from it.
/// </summary>
public class PackagingLineRowTests
{
    /// <summary>
    /// One such row used to throw on every read, and every read of all the products failed with it (Reports, Print, the
    /// Recipe API). The database refuses one now (CK_PackagingLines_AuthoredQuantity), so this is only ever an edit from
    /// before it or outside it, which should cost nothing and be there to put right.
    /// </summary>
    [Theory]
    [InlineData(PackagingLineAuthoredField.QtyPerUnit)]
    [InlineData(PackagingLineAuthoredField.UnitsPerPackagingUnit)]
    public void A_saved_line_with_a_quantity_of_0_reads_as_costing_nothing(PackagingLineAuthoredField authoredField)
    {
        var row = PackagingLineRow.FromSaved(new PackagingLine { PackagingMaterial = Box(), AuthoredField = authoredField, QtyPerUnit = 0m, UnitsPerPackagingUnit = 0m });

        row.QtyPerUnit.Should().Be(0m);
        row.CostPerUnit.Should().Be(0m);
    }

    private static UnitOfMeasure Unit(string abbreviation) => new() { Name = abbreviation, Abbreviation = abbreviation };

    /// <summary>Ribbon at ₱0.05 a cm buffered (₱0.04 unbuffered), also bought by the 1,000 cm roll (RawMaterialUnit 7).</summary>
    private static RawMaterial Ribbon() => new()
    {
        Id = 1,
        Name = "Ribbon",
        Type = RawMaterialType.Packaging,
        BaseUnitId = 10,
        BaseUnit = Unit("cm"),
        CostPerBaseUnit = 0.04m,
        BufferedCostPerBaseUnit = 0.05m,
        Units = [new RawMaterialUnit { Id = 7, UnitOfMeasureId = 11, UnitOfMeasure = Unit("roll"), BaseUnitsPerUnit = 1000m }],
    };

    /// <summary>A gift box at ₱30 a piece, with no alternative units.</summary>
    private static RawMaterial Box() => new()
    {
        Id = 2,
        Name = "Gift Box",
        Type = RawMaterialType.Packaging,
        BaseUnitId = 12,
        BaseUnit = Unit("pc"),
        BufferedCostPerBaseUnit = 30m,
    };

    [Fact]
    public void Switched_to_unbuffered_a_row_reprices_its_units_and_its_line_and_keeps_its_unit()
    {
        var row = new PackagingLineRow(Ribbon(), 2m, unitId: 7);
        row.CostBasis.Should().Be(CostBasis.Buffered);
        (row.UnitCost, row.CostPerUnit).Should().Be((50m, 100m));
        List<string?> changed = [];
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        row.CostBasis = CostBasis.Unbuffered;

        row.UnitOptions.Select(o => o.UnitCost).Should().Equal(0.04m, 40m);
        row.UnitKey.Should().Be(7);
        (row.UnitCost, row.CostPerUnit).Should().Be((40m, 80m), "2 rolls of 1,000 cm at ₱0.04 a cm");
        changed.Should().Contain([nameof(PackagingLineRow.UnitCost), nameof(PackagingLineRow.CostPerUnit)]);

        row.CostBasis = CostBasis.Buffered;

        (row.UnitCost, row.CostPerUnit).Should().Be((50m, 100m));
    }

    [Fact]
    public void A_row_offers_its_materials_base_unit_first_then_its_alternative_units()
    {
        var ribbon = Ribbon();

        var row = new PackagingLineRow(ribbon, 30m);

        row.UnitOptions.Select(o => (o.Key, o.Abbreviation, o.BaseUnitsPerUnit, o.UnitCost)).Should().Equal(
            (PackagingUnitChoice.BaseUnitKey, "cm", 1m, 0.05m),
            (7, "roll", 1000m, 50m));
        row.UnitKey.Should().Be(PackagingUnitChoice.BaseUnitKey, "a line with no unit is in the base unit");
        row.UnitId.Should().BeNull();
    }

    [Fact]
    public void A_quantity_in_an_alternative_unit_converts_to_base_units_and_is_costed_through_them()
    {
        var row = new PackagingLineRow(Ribbon(), 1m, unitId: 7);

        row.UnitKey.Should().Be(7);
        row.UnitId.Should().Be(7, "the line stores the unit it was typed in, not the base-unit figure");
        row.QtyPerUnit.Should().Be(1m);
        row.BaseQty.Should().Be(1000m);
        row.UnitCost.Should().Be(50m, "one roll costs ₱50.00");
        row.CostPerUnit.Should().Be(50m, "1 roll is 1,000 cm at ₱0.05");
    }

    [Fact]
    public void UnitCost_is_what_one_of_the_picked_unit_costs_not_the_lines_total()
    {
        // Two rolls: UnitCost stays what ONE roll costs, while CostPerUnit (the line's total) is for both.
        var row = new PackagingLineRow(Ribbon(), 2m, unitId: 7);

        row.UnitCost.Should().Be(50m, "what one roll costs, whatever the quantity");
        row.CostPerUnit.Should().Be(100m, "2 rolls are 2,000 cm at ₱0.05");
    }

    [Fact]
    public void Picking_a_unit_reprices_the_row_without_the_quantity_changing()
    {
        var row = new PackagingLineRow(Ribbon(), 2m);

        row.UnitCost.Should().Be(0.05m, "a cm costs ₱0.05");
        row.CostPerUnit.Should().Be(0.10m, "2 cm at ₱0.05");

        row.UnitKey = 7;

        row.QtyPerUnit.Should().Be(2m, "what was typed stays typed");
        row.BaseQty.Should().Be(2000m);
        row.UnitCost.Should().Be(50m, "now priced per roll, not per cm");
        row.CostPerUnit.Should().Be(100m, "2 rolls are 2,000 cm at ₱0.05");
    }

    [Fact]
    public void A_material_without_the_row_s_unit_puts_the_row_back_in_its_base_unit()
    {
        var box = Box();
        var row = new PackagingLineRow(id => id == box.Id ? box : null)
        {
            PackagingMaterialId = box.Id,
            UnitKey = 7,
        };

        row.UnitKey.Should().Be(PackagingUnitChoice.BaseUnitKey, "the box has no roll, so the row can't be in one");
        row.UnitId.Should().BeNull();
        var onlyOption = row.UnitOptions.Should().ContainSingle().Subject;
        onlyOption.Abbreviation.Should().Be("pc");
        onlyOption.UnitCost.Should().Be(30m);
    }

    [Fact]
    public void Resolve_keeps_the_unit_when_it_swaps_in_a_refreshed_copy_of_the_material()
    {
        var row = new PackagingLineRow(Ribbon(), 1m, unitId: 7);
        var refreshed = Ribbon();
        refreshed.BufferedCostPerBaseUnit = 0.06m;

        PackagingLineRow.Resolve([row], new Dictionary<int, RawMaterial> { [refreshed.Id] = refreshed });

        row.PackagingMaterial.Should().BeSameAs(refreshed);
        row.UnitKey.Should().Be(7, "the refreshed material brings the same unit back");
        row.UnitCost.Should().Be(60m, "one roll now costs ₱0.06 × 1,000");
        row.CostPerUnit.Should().Be(60m, "1,000 cm at the new ₱0.06");
    }

    [Fact]
    public void CompleteLines_carries_each_rows_unit()
    {
        var ribbon = Ribbon();
        var box = Box();
        var rows = new[] { new PackagingLineRow(ribbon, 1m, unitId: 7), new PackagingLineRow(box, 1m) };

        var lines = PackagingLineRow.CompleteLines(rows, "set");

        lines.Select(l => (l.PackagingMaterial.Id, l.UnitId, l.QtyPerUnit)).Should().Equal(
            (ribbon.Id, 7, 1m),
            (box.Id, null, 1m));
    }
}
