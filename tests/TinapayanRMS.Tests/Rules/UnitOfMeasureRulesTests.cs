using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Tests.Rules;

public class UnitOfMeasureRulesTests
{
    [Fact]
    public void NormalizeName_leaves_an_already_lowercase_name_unchanged()
    {
        UnitOfMeasureRules.NormalizeName("bag").Should().Be("bag");
    }

    [Theory]
    [InlineData("Bag", "bag")]
    [InlineData("BAG", "bag")]
    [InlineData("bAg", "bag")]
    public void NormalizeName_lowercases_a_name_with_any_uppercase_letter(string name, string expected)
    {
        UnitOfMeasureRules.NormalizeName(name).Should().Be(expected);
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
