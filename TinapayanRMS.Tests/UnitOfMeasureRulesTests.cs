using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;

namespace TinapayanRMS.Tests;

public class UnitOfMeasureRulesTests
{
    [Fact]
    public void EnsureNameIsLowercase_accepts_an_all_lowercase_name()
    {
        var act = () => UnitOfMeasureRules.EnsureNameIsLowercase("bag");

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Bag")]
    [InlineData("BAG")]
    [InlineData("bAg")]
    public void EnsureNameIsLowercase_rejects_a_name_with_any_uppercase_letter(string name)
    {
        var act = () => UnitOfMeasureRules.EnsureNameIsLowercase(name);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureLengthUnitFieldsAreConsistent_accepts_a_length_unit_with_a_positive_factor()
    {
        var act = () => UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(isLengthUnit: true, millimetersPerUnit: 10m);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureLengthUnitFieldsAreConsistent_accepts_a_non_length_unit_with_no_factor()
    {
        var act = () => UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(isLengthUnit: false, millimetersPerUnit: null);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureLengthUnitFieldsAreConsistent_rejects_a_length_unit_with_no_factor()
    {
        var act = () => UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(isLengthUnit: true, millimetersPerUnit: null);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureLengthUnitFieldsAreConsistent_rejects_a_length_unit_with_a_zero_factor()
    {
        var act = () => UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(isLengthUnit: true, millimetersPerUnit: 0m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureLengthUnitFieldsAreConsistent_rejects_a_length_unit_with_a_negative_factor()
    {
        var act = () => UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(isLengthUnit: true, millimetersPerUnit: -1m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureLengthUnitFieldsAreConsistent_rejects_a_non_length_unit_with_a_stray_factor()
    {
        var act = () => UnitOfMeasureRules.EnsureLengthUnitFieldsAreConsistent(isLengthUnit: false, millimetersPerUnit: 10m);

        act.Should().Throw<InvalidOperationException>();
    }
}
