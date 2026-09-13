using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Rules;

public class PackagingMaterialRulesTests
{
    [Fact]
    public void ApplyLatestPrice_divides_the_price_by_the_base_units_in_its_unit()
    {
        var packagingMaterial = new PackagingMaterial();

        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latestPrice: 45m, baseUnitsPerUnit: 1000m); // ₱45 a 1,000 cm roll

        packagingMaterial.CostPerBaseUnit.Should().Be(0.045m);
    }

    [Fact]
    public void ApplyLatestPrice_treats_a_price_with_no_unit_as_per_base_unit()
    {
        var packagingMaterial = new PackagingMaterial();

        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latestPrice: 0.3m, baseUnitsPerUnit: null);

        packagingMaterial.CostPerBaseUnit.Should().Be(0.3m);
    }

    [Fact]
    public void ApplyLatestPrice_with_no_price_zeroes_the_cost()
    {
        var packagingMaterial = new PackagingMaterial { CostPerBaseUnit = 0.045m };

        PackagingMaterialRules.ApplyLatestPrice(packagingMaterial, latestPrice: null, baseUnitsPerUnit: null);

        packagingMaterial.CostPerBaseUnit.Should().Be(0m);
    }

    [Fact]
    public void EnsureNotTheBaseUnit_accepts_another_unit()
    {
        var act = () => PackagingMaterialRules.EnsureNotTheBaseUnit(baseUnitId: 1, unitOfMeasureId: 2);

        act.Should().NotThrow();
    }

    [Fact]
    public void EnsureNotTheBaseUnit_rejects_the_base_unit_itself()
    {
        var act = () => PackagingMaterialRules.EnsureNotTheBaseUnit(baseUnitId: 1, unitOfMeasureId: 1);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureBaseUnitCanChange_accepts_a_material_with_nothing_measured_in_it_yet()
    {
        var act = () => PackagingMaterialRules.EnsureBaseUnitCanChange(hasUnits: false, hasPrices: false, hasLines: false);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void EnsureBaseUnitCanChange_rejects_a_material_with_units_prices_or_lines(bool hasUnits, bool hasPrices, bool hasLines)
    {
        var act = () => PackagingMaterialRules.EnsureBaseUnitCanChange(hasUnits, hasPrices, hasLines);

        act.Should().Throw<InvalidOperationException>();
    }
}
