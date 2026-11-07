using AwesomeAssertions;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

public class GridLayoutStoreTests
{
    [Test]
    public void A_layout_reads_back_as_it_was_written()
    {
        var viewState = new InMemoryViewStateStore();
        var layout = new GridLayout(
            ["LaborCost", "OverheadCost"],
            [new GridSort("MarkupPercent", IsDescending: false), new GridSort("ProductName", IsDescending: true)],
            [
                new GridColumnFilter("Stage", [new GridFilterCondition("Equals", "Or", "StringTyped", false, "Value", "Production")]),
                new GridColumnFilter("Category",
                [
                    new GridFilterCondition("Equals", "Or", "StringTyped", false, "Value", "Breads"),
                    new GridFilterCondition("Equals", "Or", "StringTyped", false, "Value", null),
                ]),
                new GridColumnFilter("BaseCost", [new GridFilterCondition("GreaterThan", "And", "StronglyTyped", false, "Value", 12.5m)]),
            ]);

        new GridLayoutStore(viewState, "report").Write(layout);
        var read = new GridLayoutStore(viewState, "report").Read();

        read.HiddenColumns.Should().Equal("LaborCost", "OverheadCost");
        read.Sorts.Should().Equal(layout.Sorts);
        read.Filters.Should().HaveCount(3);
        read.Filters[1].Conditions.Should().Equal(layout.Filters[1].Conditions);
        read.Filters[2].Conditions.Single().Value.Should().Be(12.5m);
    }

    [Test]
    public void A_grid_never_laid_out_keeps_its_own_columns_and_has_no_sort_or_filter()
    {
        var read = new GridLayoutStore(new InMemoryViewStateStore(), "report").Read();

        read.HiddenColumns.Should().BeNull("the grid's XAML decides until the user chooses");
        read.Sorts.Should().BeEmpty();
        read.Filters.Should().BeEmpty();
    }

    [Test]
    public void A_line_it_cannot_read_is_left_out()
    {
        var viewState = new InMemoryViewStateStore(new Dictionary<string, IReadOnlyList<string>>
        {
            ["report-sort"] = ["Name\tasc", "garbled"],
            ["report-filters"] = ["Stage\tEquals\tOr\tStringTyped\t0\tValue\ts:Production", "Stage\tEquals\tOr\tStringTyped\t0\tValue\tz:what"],
        });

        var read = new GridLayoutStore(viewState, "report").Read();

        read.Sorts.Should().Equal(new GridSort("Name", IsDescending: false));
        read.Filters.Single().Conditions.Single().Value.Should().Be("Production");
    }
}
