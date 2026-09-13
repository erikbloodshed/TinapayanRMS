using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Entities;

namespace TinapayanRMS.Tests.Rules;

public class PriceHistoryRulesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void EnsureValidPrice_rejects_non_positive_prices(decimal price)
    {
        var act = () => PriceHistoryRules.EnsureValidPrice(price);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void EnsureValidPrice_accepts_a_positive_price()
    {
        var act = () => PriceHistoryRules.EnsureValidPrice(0.01m);

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(null, 0.0)]
    [InlineData(90.0, 90.0)]
    public void IsNewPrice_is_false_when_the_entered_price_matches_the_current_one(double? currentPrice, double? enteredPrice)
    {
        PriceHistoryRules.IsNewPrice((decimal?)currentPrice, (decimal?)enteredPrice).Should().BeFalse();
    }

    [Theory]
    [InlineData(null, 90.0)]
    [InlineData(90.0, 95.5)]
    [InlineData(90.0, null)]
    [InlineData(90.0, 0.0)]
    public void IsNewPrice_is_true_when_the_entered_price_differs(double? currentPrice, double? enteredPrice)
    {
        PriceHistoryRules.IsNewPrice((decimal?)currentPrice, (decimal?)enteredPrice).Should().BeTrue();
    }

    [Fact]
    public void FindLatest_picks_the_most_recent_date()
    {
        RawMaterialPriceHistory[] entries =
        [
            new() { Id = 1, Date = new DateOnly(2026, 1, 1), Price = 80m },
            new() { Id = 2, Date = new DateOnly(2026, 3, 1), Price = 90m },
            new() { Id = 3, Date = new DateOnly(2026, 2, 1), Price = 85m },
        ];

        var latest = PriceHistoryRules.FindLatest(entries);

        latest.Should().NotBeNull();
        latest!.Price.Should().Be(90m);
    }

    [Fact]
    public void FindLatest_breaks_a_tied_date_by_the_highest_id()
    {
        RawMaterialPriceHistory[] entries =
        [
            new() { Id = 1, Date = new DateOnly(2026, 1, 1), Price = 80m },
            new() { Id = 5, Date = new DateOnly(2026, 1, 1), Price = 82m },
            new() { Id = 3, Date = new DateOnly(2026, 1, 1), Price = 81m },
        ];

        var latest = PriceHistoryRules.FindLatest(entries);

        latest.Should().NotBeNull();
        latest!.Id.Should().Be(5);
    }

    [Fact]
    public void FindLatest_returns_null_for_no_entries()
    {
        PriceHistoryRules.FindLatest(Array.Empty<RawMaterialPriceHistory>()).Should().BeNull();
    }

    [Fact]
    public void FindLatestAfterAdding_picks_an_unsaved_entry_on_the_same_date_as_the_current_latest()
    {
        // The unsaved entry's id is still 0, but it will be the most recently added once saved.
        var currentLatest = new RawMaterialPriceHistory { Id = 11, Date = new DateOnly(2026, 9, 13), Price = 13500m };
        var added = new RawMaterialPriceHistory { Date = new DateOnly(2026, 9, 13), Price = 13600m };

        PriceHistoryRules.FindLatestAfterAdding(currentLatest, added).Should().BeSameAs(added);
    }

    [Fact]
    public void FindLatestAfterAdding_keeps_the_current_latest_when_the_added_entry_is_older()
    {
        var currentLatest = new RawMaterialPriceHistory { Id = 11, Date = new DateOnly(2026, 9, 13), Price = 13500m };
        var added = new RawMaterialPriceHistory { Date = new DateOnly(2026, 9, 1), Price = 13000m };

        PriceHistoryRules.FindLatestAfterAdding(currentLatest, added).Should().BeSameAs(currentLatest);
    }

    [Fact]
    public void FindLatestAfterAdding_picks_the_added_entry_when_there_is_no_other()
    {
        var added = new RawMaterialPriceHistory { Date = new DateOnly(2026, 9, 1), Price = 13000m };

        PriceHistoryRules.FindLatestAfterAdding(null, added).Should().BeSameAs(added);
    }
}
