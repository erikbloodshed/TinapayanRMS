using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class CostingRulesTests
{
    public static IEnumerable<(decimal, decimal, decimal)> PricesRoundedUp() =>
    [
        (43.24m, 0.25m, 43.25m),
        (43.25m, 0.25m, 43.25m),
        (43.24m, 1m, 44m),
        (43.24m, 5m, 45m),
        (43.2401m, 0m, 43.2401m),
    ];

    [Test]
    [MethodDataSource(nameof(PricesRoundedUp))]
    public void RoundPriceUp_goes_up_to_the_next_multiple_never_down(decimal price, decimal step, decimal expected) =>
        CostingRules.RoundPriceUp(price, step).Should().Be(expected);

    [Test]
    public void EnsureValidPriceRoundingStep_takes_only_the_offered_steps()
    {
        foreach (var step in CostingRules.PriceRoundingSteps)
        {
            CostingRules.EnsureValidPriceRoundingStep(step);
        }

        var act = () => CostingRules.EnsureValidPriceRoundingStep(0.3m);
        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidParameters_rejects_negative_labor_cost()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: -1m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: 0m);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidParameters_rejects_negative_overhead_cost()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: -1m, administrativePercent: 0m, marginPercent: 0m);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidParameters_rejects_negative_administrative_cost()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: 0m, administrativePercent: -1m, marginPercent: 0m);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidParameters_rejects_negative_margin_percent()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: -5m);

        act.Should().Throw<BusinessRuleException>();
    }

    [Test]
    public void EnsureValidVat_rejects_a_negative_vat_and_accepts_none()
    {
        var negative = () => CostingRules.EnsureValidVat(-1m);
        var none = () => CostingRules.EnsureValidVat(0m);

        negative.Should().Throw<BusinessRuleException>();
        none.Should().NotThrow();
    }

    [Test]
    public void EnsureValidParameters_accepts_zeroes()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: 0m);

        act.Should().NotThrow();
    }

    [Test]
    public void CalculateCostFromSubAssemblyPercent_is_that_percentage_of_the_sub_assembly_cost()
    {
        CostingRules.CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece: 20m, percent: 25m).Should().Be(5m);
    }

    [Test]
    public void CalculateCostFromSubAssemblyPercent_is_zero_with_no_sub_assembly_cost()
    {
        CostingRules.CalculateCostFromSubAssemblyPercent(subAssemblyCostPerPiece: 0m, percent: 25m).Should().Be(0m);
    }

    [Test]
    public void CalculateSellingPrice_applies_cost_plus_markup_then_VAT_on_top()
    {
        // Sub-assemblies ₱20 + Labor 25% (₱5) + Overhead 15% (₱3) = ₱28 base cost.
        // Margin 40% markup on cost -> ₱39.20 pre-VAT price.
        // VAT 12% added on top -> ₱43.904 final selling price.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 20m, packagingCostPerPiece: 0m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 0m, marginPercent: 40m, vatPercent: 12m);

        baseCost.Should().Be(28m);
        preVatPrice.Should().Be(39.2m);
        sellingPrice.Should().Be(43.904m);
    }

    [Test]
    public void CalculateSellingPrice_adds_the_administrative_cost_to_the_base_cost_before_the_markup()
    {
        // Sub-assemblies ₱20 + Labor 25% (₱5) + Overhead 15% (₱3) + Administrative 10% (₱2) = ₱30 base cost.
        // Margin 40% markup on cost -> ₱42 pre-VAT price; VAT 12% on top -> ₱47.04.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 20m, packagingCostPerPiece: 0m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 10m, marginPercent: 40m, vatPercent: 12m);

        baseCost.Should().Be(30m);
        preVatPrice.Should().Be(42m);
        sellingPrice.Should().Be(47.04m);
    }

    [Test]
    public void CalculateSellingPrice_with_zero_margin_and_VAT_equals_base_cost()
    {
        // Sub-assemblies ₱10 + Labor 20% (₱2) + Overhead 10% (₱1) = ₱13.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 10m, packagingCostPerPiece: 0m, laborPercent: 20m, overheadPercent: 10m, administrativePercent: 0m, marginPercent: 0m, vatPercent: 0m);

        baseCost.Should().Be(13m);
        preVatPrice.Should().Be(13m);
        sellingPrice.Should().Be(13m);
    }

    [Test]
    public void CalculateSellingPrice_with_no_material_cost_has_no_labor_overhead_or_administrative_cost()
    {
        var (baseCost, _, sellingPrice) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 0m, packagingCostPerPiece: 0m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 10m, marginPercent: 40m, vatPercent: 12m);

        baseCost.Should().Be(0m);
        sellingPrice.Should().Be(0m);
    }

    [Test]
    public void CalculateSellingPrice_takes_labor_overhead_and_administrative_off_the_sub_assemblies_only()
    {
        // The three percentages pay for making the recipe, so ₱4 of packaging adds ₱4 to the base cost and
        // nothing to labor, overhead or administrative: ₱20 + 25% (₱5) + 15% (₱3) + 10% (₱2) + ₱4 = ₱34.
        var (baseCost, _, _) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 20m, packagingCostPerPiece: 4m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 10m, marginPercent: 0m, vatPercent: 0m);

        baseCost.Should().Be(34m);
    }

    [Test]
    public void CalculateSellingPrice_adds_the_wastage_on_the_sub_assemblies_to_the_base_cost()
    {
        // ₱100 of sub-assemblies at 3% wastage is ₱3 more. Labor 25% stays on the ₱100 (₱25), and the ₱4 of packaging
        // takes no wastage: ₱100 + ₱3 + ₱4 + ₱25 = ₱132. A 30% markup and 12% VAT go on that as before.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 100m, packagingCostPerPiece: 4m, laborPercent: 25m, overheadPercent: 0m, administrativePercent: 0m,
            marginPercent: 30m, vatPercent: 12m, wastagePercent: 3m);

        baseCost.Should().Be(132m);
        preVatPrice.Should().Be(171.6m);
        sellingPrice.Should().Be(192.192m);
    }

    [Test]
    public void CalculateSellingPrice_adds_no_wastage_on_packaging_alone()
    {
        var (baseCost, _, _) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 0m, packagingCostPerPiece: 4m, laborPercent: 0m, overheadPercent: 0m, administrativePercent: 0m,
            marginPercent: 0m, vatPercent: 0m, wastagePercent: 3m);

        baseCost.Should().Be(4m);
    }

    public static IEnumerable<decimal> WastagesInRange() => [0m, 3m, 100m];

    [Test]
    [MethodDataSource(nameof(WastagesInRange))]
    public void EnsureValidWastage_accepts_0_to_100(decimal wastagePercent)
    {
        var act = () => CostingRules.EnsureValidWastage(wastagePercent);

        act.Should().NotThrow();
    }

    public static IEnumerable<decimal> WastagesOutOfRange() => [-0.01m, 100.01m];

    [Test]
    [MethodDataSource(nameof(WastagesOutOfRange))]
    public void EnsureValidWastage_refuses_a_wastage_outside_0_to_100(decimal wastagePercent)
    {
        var act = () => CostingRules.EnsureValidWastage(wastagePercent);

        act.Should().Throw<BusinessRuleException>().WithMessage("*wastage*");
    }

    [Test]
    public void CalculateSellingPrice_charges_nothing_for_labor_overhead_or_administrative_on_packaging_alone()
    {
        var (baseCost, _, _) = CostingRules.CalculateSellingPrice(
            subAssemblyCostPerPiece: 0m, packagingCostPerPiece: 4m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 10m, marginPercent: 0m, vatPercent: 0m);

        baseCost.Should().Be(4m);
    }

    [Test]
    public void CalculateMarkupPercentFromPrice_takes_VAT_out_then_gives_the_markup_on_the_base_cost()
    {
        // ₱145.60 with 12% VAT is ₱130 before VAT: ₱30 on ₱100 of cost.
        CostingRules.CalculateMarkupPercentFromPrice(baseCost: 100m, sellingPrice: 145.60m, vatPercent: 12m).Should().Be(30m);
    }

    [Test]
    public void CalculateMarkupPercentFromPrice_gives_back_the_margin_of_the_price_CalculateSellingPrice_works_out()
    {
        var (baseCost, _, sellingPrice) = CostingRules.CalculateSellingPrice(4.50m, 0m, 25m, 15m, 10m, 30m, 12m);

        CostingRules.CalculateMarkupPercentFromPrice(baseCost, sellingPrice, vatPercent: 12m).Should().BeApproximately(30m, 0.0000001m);
    }

    [Test]
    public void CalculateMarkupPercentFromPrice_is_negative_for_a_price_under_the_cost()
    {
        CostingRules.CalculateMarkupPercentFromPrice(baseCost: 100m, sellingPrice: 89.60m, vatPercent: 12m).Should().Be(-20m);
    }

    [Test]
    public void PreVatPriceFrom_takes_the_VAT_out_of_a_price()
    {
        CostingRules.PreVatPriceFrom(sellingPrice: 145.60m, vatPercent: 12m).Should().Be(130m);
        CostingRules.PreVatPriceFrom(sellingPrice: 26m, vatPercent: 0m).Should().Be(26m);
    }

    [Test]
    public void CalculateMarkupPercentFromPrice_is_zero_with_no_base_cost()
    {
        CostingRules.CalculateMarkupPercentFromPrice(baseCost: 0m, sellingPrice: 10m, vatPercent: 12m).Should().Be(0m);
    }

    [Test]
    public void PercentOf_gives_the_parts_share_of_the_whole()
    {
        CostingRules.PercentOf(part: 6m, whole: 59.45m).Should().BeApproximately(10.0925m, 0.0001m);
    }

    [Test]
    public void PercentOf_is_zero_when_the_whole_is_zero()
    {
        CostingRules.PercentOf(part: 5m, whole: 0m).Should().Be(0m);
    }

    [Test]
    public void A_unit_of_eight_with_no_packaging_of_its_own_prices_at_exactly_eight_times_the_piece_price()
    {
        // Labor, overhead and administrative are percentages of the sub-assembly cost, and margin and VAT are
        // multiplicative, so pricing is linear in the sub-assembly cost. That's what lets Quantity Required be a
        // plain multiplier on the recipe, with no percentages of its own: a boxed product's own packaging is
        // the whole of the difference from a plain multiple of the single.
        var piece = CostingRules.CalculateSellingPrice(4.50m, 0m, 25m, 15m, 10m, 30m, 12m);
        var boxOfEightSubAssemblies = 4.50m * 8m;

        var boxOfEight = CostingRules.CalculateSellingPrice(boxOfEightSubAssemblies, 0m, 25m, 15m, 10m, 30m, 12m);

        boxOfEight.SellingPrice.Should().Be(piece.SellingPrice * 8m);
        boxOfEight.BaseCost.Should().Be(piece.BaseCost * 8m);
    }
}
