using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests;

public class LengthConversionTests
{
    [Fact]
    public void Convert_from_centimeters_to_millimeters()
    {
        var result = LengthConversion.Convert(5m, WellKnownUnitOfMeasureIds.Centimeter, WellKnownUnitOfMeasureIds.Millimeter);

        result.Should().Be(50m);
    }

    [Fact]
    public void Convert_from_inches_to_millimeters()
    {
        var result = LengthConversion.Convert(5m, WellKnownUnitOfMeasureIds.Inch, WellKnownUnitOfMeasureIds.Millimeter);

        result.Should().Be(127.0m);
    }

    [Fact]
    public void Convert_from_meters_to_centimeters()
    {
        var result = LengthConversion.Convert(1m, WellKnownUnitOfMeasureIds.Meter, WellKnownUnitOfMeasureIds.Centimeter);

        result.Should().Be(100m);
    }

    [Fact]
    public void Convert_from_meters_to_millimeters()
    {
        var result = LengthConversion.Convert(1m, WellKnownUnitOfMeasureIds.Meter, WellKnownUnitOfMeasureIds.Millimeter);

        result.Should().Be(1000m);
    }

    [Fact]
    public void Convert_from_inches_to_centimeters()
    {
        var result = LengthConversion.Convert(5m, WellKnownUnitOfMeasureIds.Inch, WellKnownUnitOfMeasureIds.Centimeter);

        result.Should().Be(12.7m);
    }

    [Fact]
    public void Convert_to_inches_from_centimeters()
    {
        // Inch is only ever tested above as a source unit — this exercises it as the
        // destination of the mm-normalize-then-divide step in Convert.
        var result = LengthConversion.Convert(2.54m, WellKnownUnitOfMeasureIds.Centimeter, WellKnownUnitOfMeasureIds.Inch);

        result.Should().Be(1m);
    }

    [Fact]
    public void Convert_same_unit_returns_the_same_quantity_unchanged()
    {
        var result = LengthConversion.Convert(12.7m, WellKnownUnitOfMeasureIds.Centimeter, WellKnownUnitOfMeasureIds.Centimeter);

        result.Should().Be(12.7m);
    }

    [Fact]
    public void Convert_round_trips_through_two_units_without_drift()
    {
        var centimeters = LengthConversion.Convert(80m, WellKnownUnitOfMeasureIds.Millimeter, WellKnownUnitOfMeasureIds.Centimeter);
        var backToMillimeters = LengthConversion.Convert(centimeters, WellKnownUnitOfMeasureIds.Centimeter, WellKnownUnitOfMeasureIds.Millimeter);

        backToMillimeters.Should().Be(80m);
    }

    [Fact]
    public void IsLengthUnit_is_true_for_all_four_protected_units()
    {
        foreach (var id in WellKnownUnitOfMeasureIds.ProtectedLengthUnitIds)
        {
            LengthConversion.IsLengthUnit(id).Should().BeTrue();
        }
    }

    [Fact]
    public void IsLengthUnit_is_false_for_gram()
    {
        LengthConversion.IsLengthUnit(5).Should().BeFalse();
    }

    [Fact]
    public void Convert_throws_for_a_unit_that_is_not_a_length_unit()
    {
        var act = () => LengthConversion.Convert(1m, fromUnitOfMeasureId: 5, toUnitOfMeasureId: WellKnownUnitOfMeasureIds.Centimeter);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
