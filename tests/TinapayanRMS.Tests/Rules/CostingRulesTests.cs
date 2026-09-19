using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Tests.Rules;

public class CostingRulesTests
{
    [Fact]
    public void EnsureValidParameters_rejects_negative_labor_cost()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: -1m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: 0m, vatPercent: 0m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidParameters_rejects_negative_overhead_cost()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: -1m, administrativePercent: 0m, marginPercent: 0m, vatPercent: 0m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidParameters_rejects_negative_administrative_cost()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: 0m, administrativePercent: -1m, marginPercent: 0m, vatPercent: 0m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidParameters_rejects_negative_margin_percent()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: -5m, vatPercent: 0m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidParameters_rejects_negative_vat_percent()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: 0m, vatPercent: -1m);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidParameters_accepts_zeroes()
    {
        var act = () => CostingRules.EnsureValidParameters(laborPercent: 0m, overheadPercent: 0m, administrativePercent: 0m, marginPercent: 0m, vatPercent: 0m);

        act.Should().NotThrow();
    }

    [Fact]
    public void CalculateCostFromMaterialPercent_is_that_percentage_of_the_material_cost()
    {
        CostingRules.CalculateCostFromMaterialPercent(materialCostPerPiece: 20m, percent: 25m).Should().Be(5m);
    }

    [Fact]
    public void CalculateCostFromMaterialPercent_is_zero_with_no_material_cost()
    {
        CostingRules.CalculateCostFromMaterialPercent(materialCostPerPiece: 0m, percent: 25m).Should().Be(0m);
    }

    [Fact]
    public void CalculateSellingPrice_applies_cost_plus_markup_then_VAT_on_top()
    {
        // Material ₱20 + Labor 25% (₱5) + Overhead 15% (₱3) = ₱28 base cost.
        // Margin 40% markup on cost -> ₱39.20 pre-VAT price.
        // VAT 12% added on top -> ₱43.904 final selling price.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            materialCostPerPiece: 20m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 0m, marginPercent: 40m, vatPercent: 12m);

        baseCost.Should().Be(28m);
        preVatPrice.Should().Be(39.2m);
        sellingPrice.Should().Be(43.904m);
    }

    [Fact]
    public void CalculateSellingPrice_adds_the_administrative_cost_to_the_base_cost_before_the_markup()
    {
        // Material ₱20 + Labor 25% (₱5) + Overhead 15% (₱3) + Administrative 10% (₱2) = ₱30 base cost.
        // Margin 40% markup on cost -> ₱42 pre-VAT price; VAT 12% on top -> ₱47.04.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            materialCostPerPiece: 20m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 10m, marginPercent: 40m, vatPercent: 12m);

        baseCost.Should().Be(30m);
        preVatPrice.Should().Be(42m);
        sellingPrice.Should().Be(47.04m);
    }

    [Fact]
    public void CalculateSellingPrice_with_zero_margin_and_VAT_equals_base_cost()
    {
        // Material ₱10 + Labor 20% (₱2) + Overhead 10% (₱1) = ₱13.
        var (baseCost, preVatPrice, sellingPrice) = CostingRules.CalculateSellingPrice(
            materialCostPerPiece: 10m, laborPercent: 20m, overheadPercent: 10m, administrativePercent: 0m, marginPercent: 0m, vatPercent: 0m);

        baseCost.Should().Be(13m);
        preVatPrice.Should().Be(13m);
        sellingPrice.Should().Be(13m);
    }

    [Fact]
    public void CalculateSellingPrice_with_no_material_cost_has_no_labor_overhead_or_administrative_cost()
    {
        var (baseCost, _, sellingPrice) = CostingRules.CalculateSellingPrice(
            materialCostPerPiece: 0m, laborPercent: 25m, overheadPercent: 15m, administrativePercent: 10m, marginPercent: 40m, vatPercent: 12m);

        baseCost.Should().Be(0m);
        sellingPrice.Should().Be(0m);
    }

    [Fact]
    public void CalculateProfitMarginPercent_is_the_markups_share_of_the_pre_VAT_price()
    {
        // ₱100 base cost at a 30% markup sells for ₱130 before VAT: ₱30 profit, 30 / 130 of the price.
        CostingRules.CalculateProfitMarginPercent(baseCost: 100m, preVatPrice: 130m)
            .Should().BeApproximately(23.0769m, 0.0001m);
    }

    [Fact]
    public void CalculateProfitMarginPercent_is_zero_with_no_price()
    {
        CostingRules.CalculateProfitMarginPercent(baseCost: 0m, preVatPrice: 0m).Should().Be(0m);
    }

    [Fact]
    public void CalculateMarkupPercentFromPrice_takes_VAT_out_then_gives_the_markup_on_the_base_cost()
    {
        // ₱145.60 with 12% VAT is ₱130 before VAT: ₱30 on ₱100 of cost.
        CostingRules.CalculateMarkupPercentFromPrice(baseCost: 100m, sellingPrice: 145.60m, vatPercent: 12m).Should().Be(30m);
    }

    [Fact]
    public void CalculateMarkupPercentFromPrice_gives_back_the_margin_of_the_price_CalculateSellingPrice_works_out()
    {
        var (baseCost, _, sellingPrice) = CostingRules.CalculateSellingPrice(4.50m, 25m, 15m, 10m, 30m, 12m);

        CostingRules.CalculateMarkupPercentFromPrice(baseCost, sellingPrice, vatPercent: 12m).Should().BeApproximately(30m, 0.0000001m);
    }

    [Fact]
    public void CalculateMarkupPercentFromPrice_is_negative_for_a_price_under_the_cost()
    {
        CostingRules.CalculateMarkupPercentFromPrice(baseCost: 100m, sellingPrice: 89.60m, vatPercent: 12m).Should().Be(-20m);
    }

    [Fact]
    public void CalculateMarkupPercentFromPrice_is_zero_with_no_base_cost()
    {
        CostingRules.CalculateMarkupPercentFromPrice(baseCost: 0m, sellingPrice: 10m, vatPercent: 12m).Should().Be(0m);
    }

    [Fact]
    public void PercentOf_gives_the_parts_share_of_the_whole()
    {
        CostingRules.PercentOf(part: 6m, whole: 59.45m).Should().BeApproximately(10.0925m, 0.0001m);
    }

    [Fact]
    public void PercentOf_is_zero_when_the_whole_is_zero()
    {
        CostingRules.PercentOf(part: 5m, whole: 0m).Should().Be(0m);
    }

    [Fact]
    public void A_pack_with_no_box_of_its_own_prices_at_exactly_its_piece_count_times_the_piece_price()
    {
        // Labor, overhead and administrative are percentages of the material cost, and margin and VAT are
        // multiplicative, so pricing is linear in the material cost. That's what makes a pack's price come
        // out of the same CalculateSellingPrice as a piece's, with no pack-specific percentages: a pack's
        // own box is the whole of the difference from a plain multiple.
        var piece = CostingRules.CalculateSellingPrice(4.50m, 25m, 15m, 10m, 30m, 12m);
        var packMaterial = ProductRules.CalculatePackMaterialCost(4.50m, 8, []);

        var pack = CostingRules.CalculateSellingPrice(packMaterial, 25m, 15m, 10m, 30m, 12m);

        pack.SellingPrice.Should().Be(piece.SellingPrice * 8m);
        pack.BaseCost.Should().Be(piece.BaseCost * 8m);
    }
}
