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
}
