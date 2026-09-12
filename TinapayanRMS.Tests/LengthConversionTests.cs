using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;
using TinapayanRMS.Domain;

namespace TinapayanRMS.Tests;

public class LengthConversionTests
{
    private static readonly UnitOfMeasure Millimeter = new() { Id = 1, Name = "millimeter", Abbreviation = "mm", IsLengthUnit = true, MillimetersPerUnit = 1m };
    private static readonly UnitOfMeasure Centimeter = new() { Id = 2, Name = "centimeter", Abbreviation = "cm", IsLengthUnit = true, MillimetersPerUnit = 10m };
    private static readonly UnitOfMeasure Inch = new() { Id = 3, Name = "inch", Abbreviation = "in", IsLengthUnit = true, MillimetersPerUnit = 25.4m };
    private static readonly UnitOfMeasure Meter = new() { Id = 4, Name = "meter", Abbreviation = "m", IsLengthUnit = true, MillimetersPerUnit = 1000m };
    private static readonly UnitOfMeasure Gram = new() { Id = 5, Name = "gram", Abbreviation = "g" };

    [Fact]
    public void Convert_from_centimeters_to_millimeters()
    {
        var result = LengthConversion.Convert(5m, Centimeter, Millimeter);

        result.Should().Be(50m);
    }

    [Fact]
    public void Convert_from_inches_to_millimeters()
    {
        var result = LengthConversion.Convert(5m, Inch, Millimeter);

        result.Should().Be(127.0m);
    }

    [Fact]
    public void Convert_from_meters_to_centimeters()
    {
        var result = LengthConversion.Convert(1m, Meter, Centimeter);

        result.Should().Be(100m);
    }

    [Fact]
    public void Convert_from_meters_to_millimeters()
    {
        var result = LengthConversion.Convert(1m, Meter, Millimeter);

        result.Should().Be(1000m);
    }

    [Fact]
    public void Convert_from_inches_to_centimeters()
    {
        var result = LengthConversion.Convert(5m, Inch, Centimeter);

        result.Should().Be(12.7m);
    }

    [Fact]
    public void Convert_to_inches_from_centimeters()
    {
        // Inch is only ever tested above as a source unit — this exercises it as the
        // destination of the mm-normalize-then-divide step in Convert.
        var result = LengthConversion.Convert(2.54m, Centimeter, Inch);

        result.Should().Be(1m);
    }

    [Fact]
    public void Convert_same_unit_returns_the_same_quantity_unchanged()
    {
        var result = LengthConversion.Convert(12.7m, Centimeter, Centimeter);

        result.Should().Be(12.7m);
    }

    [Fact]
    public void Convert_round_trips_through_two_units_without_drift()
    {
        var centimeters = LengthConversion.Convert(80m, Millimeter, Centimeter);
        var backToMillimeters = LengthConversion.Convert(centimeters, Centimeter, Millimeter);

        backToMillimeters.Should().Be(80m);
    }

    [Fact]
    public void Convert_throws_for_a_unit_that_is_not_a_length_unit()
    {
        var act = () => LengthConversion.Convert(1m, Gram, Centimeter);

        act.Should().Throw<InvalidOperationException>();
    }
}
