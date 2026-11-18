using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class UnitConversionRulesTests
{
    [Test]
    public void ToBaseQuantity_multiplies_by_the_units_multiplier()
    {
        // 2 bags of 25,000 g.
        UnitConversionRules.ToBaseQuantity(2m, baseUnitsPerUnit: 25000m).Should().Be(50000m);
    }

    [Test]
    public void ToBaseQuantity_leaves_a_base_unit_quantity_as_it_is()
    {
        UnitConversionRules.ToBaseQuantity(500m, baseUnitsPerUnit: null).Should().Be(500m);
    }

    [Test]
    public void CostPerBaseUnit_divides_the_price_by_the_units_multiplier()
    {
        // ₱1,250 for a 25,000 g bag, the mockup's flour.
        UnitConversionRules.CostPerBaseUnit(1250m, baseUnitsPerUnit: 25000m).Should().Be(0.05m);
    }

    [Test]
    public void CostPerBaseUnit_leaves_a_base_unit_price_as_it_is()
    {
        UnitConversionRules.CostPerBaseUnit(0.05m, baseUnitsPerUnit: null).Should().Be(0.05m);
    }

    [Test]
    public void EnsureValidMultiplier_accepts_a_positive_multiplier()
    {
        var act = () => UnitConversionRules.EnsureValidMultiplier(0.5m);

        act.Should().NotThrow();
    }

    [Test]
    [Arguments(0)]
    [Arguments(-1)]
    public void EnsureValidMultiplier_rejects_a_zero_or_negative_multiplier(decimal multiplier)
    {
        var act = () => UnitConversionRules.EnsureValidMultiplier(multiplier);

        act.Should().Throw<BusinessRuleException>();
    }
}
