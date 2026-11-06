using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.ApplicationCore.Tests.Rules;

public class PriceChangeRulesTests
{
    [Test]
    public void A_change_in_percent_and_a_new_price_follow_from_each_other()
    {
        PriceChangeRules.NewPrice(1250m, 10m).Should().Be(1375m);
        PriceChangeRules.NewPrice(1250m, -20m).Should().Be(1000m);
        PriceChangeRules.ChangePercent(1250m, 1375m).Should().Be(10m);
        PriceChangeRules.ChangePercent(1250m, 1000m).Should().Be(-20m);
    }

    [Test]
    public void The_price_to_keep_a_markup_moves_with_the_base_cost_and_earns_the_same_markup()
    {
        // ₱10 on a ₱6 base cost, the base cost up 10% to ₱6.60: ₱11 keeps the markup, VAT and all.
        PriceChangeRules.PriceToKeepMarkup(10m, 6m, 6.6m).Should().Be(11m);
        PriceChangeRules.PriceToKeepMarkup(10m, 6m, 5.4m).Should().Be(9m, "a fall lowers it as much");
        PriceChangeRules.PriceToKeepMarkup(10m, 6m, 6m).Should().Be(10m, "an untouched product keeps its price");

        const decimal vat = 12m;
        CostingRules.CalculateMarkupPercentFromPrice(6.6m, PriceChangeRules.PriceToKeepMarkup(10m, 6m, 6.6m)!.Value, vat)
            .Should().BeApproximately(CostingRules.CalculateMarkupPercentFromPrice(6m, 10m, vat), 0.0000001m);
    }

    [Test]
    public void There_is_no_price_to_keep_a_markup_without_a_current_price_or_a_base_cost()
    {
        PriceChangeRules.PriceToKeepMarkup(null, 6m, 6.6m).Should().BeNull();
        PriceChangeRules.PriceToKeepMarkup(10m, 0m, 6.6m).Should().BeNull();
    }

    [Test]
    public void A_fall_of_100_percent_or_more_and_a_price_of_nothing_are_refused()
    {
        var all = () => PriceChangeRules.EnsureValidChange(-100m);
        var most = () => PriceChangeRules.EnsureValidChange(-99.99m);
        var nothing = () => PriceChangeRules.EnsureValidNewPrice(0m);

        all.Should().Throw<BusinessRuleException>().WithMessage(PriceChangeRules.FallTooFarText);
        most.Should().NotThrow();
        nothing.Should().Throw<BusinessRuleException>().WithMessage(PriceChangeRules.NewPriceTooLowText);
    }

    [Test]
    public void A_priced_materials_cost_and_buffered_cost_move_by_the_same_factor()
    {
        // Flour at 1,250 a 25 kg sack: 0.05 a gram, held at a buffered 0.06. Up 10% to 1,375.
        var flour = new RawMaterial { CostPerBaseUnit = 0.05m, BufferedCostPerBaseUnit = 0.06m, CostBufferPercent = 10m };

        PriceChangeRules.CostsAfter(flour, 1250m, 1375m, 25000m).Should().Be((0.055m, 0.066m));
    }

    [Test]
    public void A_material_with_no_price_is_priced_as_its_first_price_would_be()
    {
        var flour = new RawMaterial { CostBufferPercent = 10m };

        PriceChangeRules.CostsAfter(flour, null, 1250m, 25000m).Should().Be((0.05m, 0.055m));
    }
}
