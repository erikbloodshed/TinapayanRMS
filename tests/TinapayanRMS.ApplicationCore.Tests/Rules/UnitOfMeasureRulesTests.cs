using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

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

    [Theory]
    [InlineData("bag ", "bag")]
    [InlineData("  Bag", "bag")]
    public void NormalizeName_takes_off_spaces_at_either_end(string name, string expected)
    {
        UnitOfMeasureRules.NormalizeName(name).Should().Be(expected);
    }

    [Theory]
    [InlineData("kg", "kg")]
    [InlineData("kg ", "kg")]
    [InlineData(" Kg ", "Kg")]
    public void NormalizeAbbreviation_takes_off_spaces_at_either_end_and_keeps_its_case(string abbreviation, string expected)
    {
        UnitOfMeasureRules.NormalizeAbbreviation(abbreviation).Should().Be(expected);
    }
}
