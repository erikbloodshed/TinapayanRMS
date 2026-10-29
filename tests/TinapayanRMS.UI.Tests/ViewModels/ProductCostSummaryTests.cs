using AwesomeAssertions;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.ViewModels;

public class ProductCostSummaryTests
{
    [Theory]
    [InlineData(26.00, false)]
    [InlineData(25.99, true)]
    [InlineData(25.9999, false)]
    [InlineData(30.00, false)]
    public void A_current_price_is_short_of_the_target_markup_when_it_earns_less_as_shown(double price, bool isShort)
    {
        // A base cost of 20.00 and a target markup of 30%, no VAT: 26.00 earns exactly 30%, and 25.9999 rounds to it.
        var costs = new ProductCostSummary([("Dough", 20m)], 1, [], 0m, 0m, 0m, 30m, 0m, (decimal)price);

        costs.IsBelowTargetMarkup.Should().Be(isShort);
    }

    [Fact]
    public void No_current_price_is_never_short_and_the_report_says_by_how_much_one_is()
    {
        new ProductCostSummary([("Dough", 20m)], 1, [], 0m, 0m, 0m, 30m, 0m, null).IsBelowTargetMarkup.Should().BeFalse();

        var row = new ProductCostRow(new Product { Name = "Pandesal" }, new ProductCostSummary([("Dough", 20m)], 1, [], 0m, 0m, 0m, 30m, 0m, 25m));
        row.TargetMarkupPercent.Should().Be(30m);
        row.ShortOfTargetMarkup.Should().Be(5m, "25.00 on 20.00 earns 25%");
        new ProductCostRow(new Product { Name = "Pandesal" }, new ProductCostSummary([("Dough", 20m)], 1, [], 0m, 0m, 0m, 30m, 0m, 27m))
            .ShortOfTargetMarkup.Should().BeNull();
    }
}
