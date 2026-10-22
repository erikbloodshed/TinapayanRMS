using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class PriceScenarioRulesTests
{
    private static PriceScenarioLine Percent(int materialId, decimal percent) => new() { RawMaterialId = materialId, ChangePercent = percent };

    private static PriceScenarioLine Price(int materialId, decimal price) => new() { RawMaterialId = materialId, NewPrice = price };

    [Fact]
    public void A_named_scenario_of_changes_typed_in_percent_or_as_new_prices_is_valid()
    {
        var valid = () => PriceScenarioRules.EnsureValid("Supplier A, Nov", [Percent(1, 10m), Price(2, 1375m), Percent(3, -99.99m)]);

        valid.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_scenario_needs_a_name(string name)
    {
        var act = () => PriceScenarioRules.EnsureValid(name, [Percent(1, 10m)]);

        act.Should().Throw<BusinessRuleException>().WithMessage(PriceScenarioRules.NameRequiredText);
    }

    [Fact]
    public void A_name_longer_than_its_column_is_refused_but_spaces_around_it_dont_count()
    {
        var tooLong = () => PriceScenarioRules.EnsureValid(new string('a', PriceScenarioRules.NameMaxLength + 1), [Percent(1, 10m)]);
        var padded = () => PriceScenarioRules.EnsureValid($"  {new string('a', PriceScenarioRules.NameMaxLength)}  ", [Percent(1, 10m)]);

        tooLong.Should().Throw<BusinessRuleException>().WithMessage(PriceScenarioRules.NameTooLongText);
        padded.Should().NotThrow();
    }

    [Fact]
    public void A_scenario_with_no_changes_or_a_material_twice_is_refused()
    {
        var empty = () => PriceScenarioRules.EnsureValid("Empty", []);
        var twice = () => PriceScenarioRules.EnsureValid("Twice", [Percent(1, 10m), Price(1, 1375m)]);

        empty.Should().Throw<BusinessRuleException>().WithMessage(PriceScenarioRules.NoChangesText);
        twice.Should().Throw<BusinessRuleException>().WithMessage(PriceScenarioRules.RepeatedMaterialText);
    }

    [Fact]
    public void Each_line_keeps_exactly_one_of_its_change_and_its_new_price_and_one_that_can_be()
    {
        var neither = () => PriceScenarioRules.EnsureValid("Neither", [new PriceScenarioLine { RawMaterialId = 1 }]);
        var both = () => PriceScenarioRules.EnsureValid("Both", [new PriceScenarioLine { RawMaterialId = 1, ChangePercent = 10m, NewPrice = 1375m }]);
        var allGone = () => PriceScenarioRules.EnsureValid("Free", [Percent(1, -100m)]);
        var nothing = () => PriceScenarioRules.EnsureValid("Free", [Price(1, 0m)]);

        neither.Should().Throw<BusinessRuleException>().WithMessage(PriceScenarioRules.OneTypedText);
        both.Should().Throw<BusinessRuleException>().WithMessage(PriceScenarioRules.OneTypedText);
        allGone.Should().Throw<BusinessRuleException>().WithMessage(PriceChangeRules.FallTooFarText);
        nothing.Should().Throw<BusinessRuleException>().WithMessage(PriceChangeRules.NewPriceTooLowText);
    }
}
