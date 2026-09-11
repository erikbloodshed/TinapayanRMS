using AwesomeAssertions;
using TinapayanRMS.ApplicationCore;

namespace TinapayanRMS.Tests;

public class CostingRulesTests
{
    [Fact]
    public void EnsureValidParameters_rejects_negative_labor_cost()
    {
        var act = () => CostingRules.EnsureValidParameters(laborCost: -1m, overheadCost: 0m, marginPercent: 0m, vatPercent: 0m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidParameters_rejects_negative_margin_percent()
    {
        var act = () => CostingRules.EnsureValidParameters(laborCost: 0m, overheadCost: 0m, marginPercent: -5m, vatPercent: 0m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidParameters_accepts_zeroes()
    {
        var act = () => CostingRules.EnsureValidParameters(laborCost: 0m, overheadCost: 0m, marginPercent: 0m, vatPercent: 0m);

        act.Should().NotThrow();
    }

    [Fact]
    public void CalculateSellingPrice_applies_cost_plus_markup_then_VAT_on_top()
    {
        // Material ₱20 + Labor ₱5 + Overhead ₱3 = ₱28 base cost.
        // Margin 40% markup on cost -> ₱39.20 pre-VAT price.
        // VAT 12% added on top -> ₱43.904 final selling price.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            materialCostPerPiece: 20m, laborCost: 5m, overheadCost: 3m, marginPercent: 40m, vatPercent: 12m);

        baseCost.Should().Be(28m);
        preVatPrice.Should().Be(39.2m);
        sellingPrice.Should().Be(43.904m);
    }

    [Fact]
    public void CalculateSellingPrice_with_zero_margin_and_VAT_equals_base_cost()
    {
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            materialCostPerPiece: 10m, laborCost: 2m, overheadCost: 1m, marginPercent: 0m, vatPercent: 0m);

        baseCost.Should().Be(13m);
        preVatPrice.Should().Be(13m);
        sellingPrice.Should().Be(13m);
    }
}
