using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class UnitOfMeasureRulesTests
{
    [Test]
    public void NormalizeName_leaves_an_already_lowercase_name_unchanged()
    {
        UnitOfMeasureRules.NormalizeName("bag").Should().Be("bag");
    }

    [Test]
    [Arguments("Bag", "bag")]
    [Arguments("BAG", "bag")]
    [Arguments("bAg", "bag")]
    public void NormalizeName_lowercases_a_name_with_any_uppercase_letter(string name, string expected)
    {
        UnitOfMeasureRules.NormalizeName(name).Should().Be(expected);
    }

    [Test]
    [Arguments("bag ", "bag")]
    [Arguments("  Bag", "bag")]
    public void NormalizeName_takes_off_spaces_at_either_end(string name, string expected)
    {
        UnitOfMeasureRules.NormalizeName(name).Should().Be(expected);
    }

    [Test]
    [Arguments("kg", "kg")]
    [Arguments("kg ", "kg")]
    [Arguments(" Kg ", "Kg")]
    public void NormalizeAbbreviation_takes_off_spaces_at_either_end_and_keeps_its_case(string abbreviation, string expected)
    {
        UnitOfMeasureRules.NormalizeAbbreviation(abbreviation).Should().Be(expected);
    }
}
