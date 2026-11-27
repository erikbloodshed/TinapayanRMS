using AwesomeAssertions;

namespace TinapayanRMS.Pricing.Tests;

public class ProductCostSummaryTests
{
    public static IEnumerable<(decimal, bool)> CurrentPrices() =>
    [
        (26.00m, false),
        (25.99m, true),
        (25.9999m, false),
        (30.00m, false),
    ];

    [Test]
    [MethodDataSource(nameof(CurrentPrices))]
    public void A_current_price_is_short_of_the_target_markup_when_it_earns_less_as_shown(decimal price, bool isShort)
    {
        // A base cost of 20.00 and a target markup of 30%, no VAT: 26.00 earns exactly 30%, and 25.9999 rounds to it.
        var costs = Summary(20m, currentPrice: price);

        costs.IsBelowTargetMarkup.Should().Be(isShort);
    }

    [Test]
    public void The_selling_price_is_also_shown_rounded_up_when_rounding_is_set()
    {
        // 20.00 at a 30% markup, no VAT: 26.00 exactly, and 26.01 for 20.0077, which rounds up to 26.25 on quarters.
        var rounded = Summary(20.0077m, priceRoundingStep: 0.25m);
        rounded.RoundedSellingPrice.Should().Be(26.25m);
        rounded.PriceRoundingLabel.Should().Be("Rounded up to the next 0.25");

        var exact = Summary(20.0077m);
        exact.HasPriceRounding.Should().BeFalse();
        exact.RoundedSellingPrice.Should().Be(exact.SellingPrice);
        exact.PriceRoundingLabel.Should().BeEmpty();
    }

    [Test]
    public void No_current_price_is_never_short() =>
        Summary(20m).IsBelowTargetMarkup.Should().BeFalse();

    [Test]
    public void The_current_price_splits_into_its_markup_and_VAT_beside_the_suggested_ones()
    {
        // A base cost of 100.00 at a 30% markup and 12% VAT is 130.00 + 15.60 = 145.60. A store price of 134.40 is
        // 120.00 before VAT: 20.00 over the base cost, and 14.40 of VAT.
        var costs = Summary(100m, currentPrice: 134.40m, vatPercent: 12m);

        (costs.Markup, costs.Vat, costs.SellingPrice).Should().Be((30m, 15.60m, 145.60m));
        (costs.CurrentPreVatPrice, costs.CurrentMarkup, costs.CurrentVat).Should().Be((120m, 20m, 14.40m));
        costs.CurrentMarkupPercent.Should().Be(20m);
    }

    [Test]
    public void A_current_price_under_the_base_cost_earns_a_negative_markup()
    {
        var costs = Summary(100m, currentPrice: 89.60m, vatPercent: 12m);

        (costs.CurrentMarkup, costs.CurrentVat).Should().Be((-20m, 9.60m));
        costs.IsCurrentPriceBelowCost.Should().BeTrue();
    }

    [Test]
    public void No_current_price_has_no_markup_or_VAT_to_compare()
    {
        var costs = Summary(100m, vatPercent: 12m);

        (costs.CurrentPreVatPrice, costs.CurrentMarkup, costs.CurrentVat).Should().Be(((decimal?)null, (decimal?)null, (decimal?)null));
    }

    [Test]
    public void A_current_price_that_shows_its_target_markup_at_an_exact_half_isnt_short()
    {
        // 25.997 on a base cost of 20.00 earns exactly 29.985%, which shows as 29.99%: the target, rounded half away from
        // zero as the header shows it. Rounded half to even it would be 29.98%, and flagged.
        Summary(20m, currentPrice: 25.997m, markupPercent: 29.99m).IsBelowTargetMarkup.Should().BeFalse();
        Summary(20m, currentPrice: 25.996m, markupPercent: 29.99m).IsBelowTargetMarkup.Should().BeTrue();
    }

    public static IEnumerable<(decimal, bool)> PricesWithAFloor() =>
    [
        (25.60m, false),
        (25.59m, true),
        (25.5999m, false),
        (25.99m, false),
    ];

    [Test]
    [MethodDataSource(nameof(PricesWithAFloor))]
    public void With_a_Warn_Below_Markup_under_its_target_a_price_is_short_only_under_both(decimal price, bool isShort)
    {
        // A base cost of 20.00 at a 30% target, warned below 28%: 25.60 earns exactly 28%, the least it may.
        var costs = new ProductCostSummary(new ProductCostInputs
        {
            Parts = [("Dough", 20m)],
            MarkupPercent = 30m,
            MarkupFloorPercent = 28m,
            CurrentPrice = price,
        });

        costs.IsBelowTargetMarkup.Should().Be(isShort);
        costs.BelowTargetMarkupText.Should().Be(isShort ? "Less than the Markup Metric, and under 28%, the Warn Below % (Settings)." : null);
    }

    [Test]
    public void A_Warn_Below_Markup_over_its_target_never_flags_a_price_that_meets_its_target()
    {
        // Aiming for 20% on a base cost of 20.00, warned below 28%: 24.00 earns its 20%, and 23.99 falls short of it.
        ProductCostSummary Costs(decimal price) => new(new ProductCostInputs
        {
            Parts = [("Dough", 20m)],
            MarkupPercent = 20m,
            MarkupFloorPercent = 28m,
            CurrentPrice = price,
        });

        Costs(24.00m).IsBelowTargetMarkup.Should().BeFalse("it meets its own target");
        Costs(23.99m).IsBelowTargetMarkup.Should().BeTrue();
        Costs(23.99m).BelowTargetMarkupText.Should().Be("Less than the Markup Metric.", "its own target is the lower");
    }

    [Test]
    public void Without_a_Warn_Below_Markup_the_tooltip_says_only_that_its_under_the_Markup_Metric() =>
        Summary(20m, currentPrice: 25.99m).BelowTargetMarkupText.Should().Be("Less than the Markup Metric.");

    /// <summary>One section at <paramref name="doughCost"/> a piece, at a 30% markup unless another is given, with no VAT unless one is given.</summary>
    private static ProductCostSummary Summary(
        decimal doughCost, decimal? currentPrice = null, decimal priceRoundingStep = 0m, decimal vatPercent = 0m, decimal markupPercent = 30m) =>
        new(new ProductCostInputs
        {
            Parts = [("Dough", doughCost)],
            MarkupPercent = markupPercent,
            VatPercent = vatPercent,
            CurrentPrice = currentPrice,
            PriceRoundingStep = priceRoundingStep,
        });
}
