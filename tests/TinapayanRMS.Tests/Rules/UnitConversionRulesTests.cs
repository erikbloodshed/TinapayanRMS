using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Tests.Rules;

public class UnitConversionRulesTests
{
    [Fact]
    public void ToBaseQuantity_multiplies_by_the_units_multiplier()
    {
        // 2 bags of 25,000 g.
        UnitConversionRules.ToBaseQuantity(2m, baseUnitsPerUnit: 25000m).Should().Be(50000m);
    }

    [Fact]
    public void ToBaseQuantity_leaves_a_base_unit_quantity_as_it_is()
    {
        UnitConversionRules.ToBaseQuantity(500m, baseUnitsPerUnit: null).Should().Be(500m);
    }

    [Fact]
    public void CostPerBaseUnit_divides_the_price_by_the_units_multiplier()
    {
        // ₱1,250 for a 25,000 g bag, the mockup's flour.
        UnitConversionRules.CostPerBaseUnit(1250m, baseUnitsPerUnit: 25000m).Should().Be(0.05m);
    }

    [Fact]
    public void CostPerBaseUnit_leaves_a_base_unit_price_as_it_is()
    {
        UnitConversionRules.CostPerBaseUnit(0.05m, baseUnitsPerUnit: null).Should().Be(0.05m);
    }

    [Fact]
    public void EnsureValidMultiplier_accepts_a_positive_multiplier()
    {
        var act = () => UnitConversionRules.EnsureValidMultiplier(0.5m);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnsureValidMultiplier_rejects_a_zero_or_negative_multiplier(decimal multiplier)
    {
        var act = () => UnitConversionRules.EnsureValidMultiplier(multiplier);

        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void EnsureCanAddAlternativeUnit_accepts_an_item_with_fewer_than_two_units(int existingUnitCount)
    {
        var act = () => UnitConversionRules.EnsureCanAddAlternativeUnit(existingUnitCount);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public void EnsureCanAddAlternativeUnit_rejects_an_item_that_already_has_two_units(int existingUnitCount)
    {
        var act = () => UnitConversionRules.EnsureCanAddAlternativeUnit(existingUnitCount);

        act.Should().Throw<InvalidOperationException>().WithMessage("*at most 2*");
    }
}
