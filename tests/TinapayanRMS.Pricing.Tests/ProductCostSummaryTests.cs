using AwesomeAssertions;

namespace TinapayanRMS.Pricing.Tests;

public class ProductCostSummaryTests
{
    public static TheoryData<decimal, bool> CurrentPrices => new()
    {
        { 26.00m, false },
        { 25.99m, true },
        { 25.9999m, false },
        { 30.00m, false },
    };

    [Theory]
    [MemberData(nameof(CurrentPrices))]
    public void A_current_price_is_short_of_the_target_markup_when_it_earns_less_as_shown(decimal price, bool isShort)
    {
        // A base cost of 20.00 and a target markup of 30%, no VAT: 26.00 earns exactly 30%, and 25.9999 rounds to it.
        var costs = Summary(20m, currentPrice: price);

        costs.IsBelowTargetMarkup.Should().Be(isShort);
    }

    [Fact]
    public void The_selling_price_is_also_shown_rounded_up_when_rounding_is_set()
    {
        // 20.00 at a 30% markup, no VAT: 26.00 exactly, and 26.01 for 20.0077, which rounds up to 26.25 on quarters.
        var rounded = Summary(20.0077m, priceRoundingStep: 0.25m);
        rounded.RoundedSellingPrice.Should().Be(26.25m);
        rounded.RoundedSellingPriceText.Should().Be("Rounded up to the next 0.25: 26.25");

        var exact = Summary(20.0077m);
        exact.HasPriceRounding.Should().BeFalse();
        exact.RoundedSellingPrice.Should().Be(exact.SellingPrice);
        exact.RoundedSellingPriceText.Should().BeEmpty();
    }

    [Fact]
    public void No_current_price_is_never_short() =>
        Summary(20m).IsBelowTargetMarkup.Should().BeFalse();

    /// <summary>One section at <paramref name="doughCost"/> a piece, at a 30% markup with no VAT.</summary>
    private static ProductCostSummary Summary(decimal doughCost, decimal? currentPrice = null, decimal priceRoundingStep = 0m) =>
        new(new ProductCostInputs
        {
            Parts = [("Dough", doughCost)],
            MarginPercent = 30m,
            CurrentPrice = currentPrice,
            PriceRoundingStep = priceRoundingStep,
        });
}
