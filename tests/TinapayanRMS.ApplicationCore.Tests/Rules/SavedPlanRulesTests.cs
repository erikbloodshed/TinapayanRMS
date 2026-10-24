using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class SavedPlanRulesTests
{
    private static SavedPlanLine Line(int productId, decimal units) => new() { ProductId = productId, Units = units };

    [Fact]
    public void A_named_plan_of_products_to_make_is_valid_with_a_product_on_two_lines()
    {
        var valid = () => SavedPlanRules.EnsureValid("Saturday", [Line(1, 1150m), Line(2, 0.5m), Line(1, 50m)]);

        valid.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_plan_needs_a_name(string name)
    {
        var act = () => SavedPlanRules.EnsureValid(name, [Line(1, 10m)]);

        act.Should().Throw<BusinessRuleException>().WithMessage(SavedPlanRules.NameRequiredText);
    }

    [Fact]
    public void A_plans_name_can_be_at_most_100_characters_once_trimmed()
    {
        var longest = () => SavedPlanRules.EnsureValid($"  {new string('a', SavedPlanRules.NameMaxLength)}  ", [Line(1, 10m)]);
        var tooLong = () => SavedPlanRules.EnsureValid(new string('a', SavedPlanRules.NameMaxLength + 1), [Line(1, 10m)]);

        longest.Should().NotThrow();
        tooLong.Should().Throw<BusinessRuleException>().WithMessage(SavedPlanRules.NameTooLongText);
    }

    [Fact]
    public void A_plan_needs_something_to_make()
    {
        var act = () => SavedPlanRules.EnsureValid("Saturday", []);

        act.Should().Throw<BusinessRuleException>().WithMessage(SavedPlanRules.NoLinesText);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Each_line_needs_more_than_0_units(int units)
    {
        var act = () => SavedPlanRules.EnsureValid("Saturday", [Line(1, 10m), Line(2, units)]);

        act.Should().Throw<BusinessRuleException>().WithMessage(SavedPlanRules.UnitsText);
    }
}
