using AwesomeAssertions;
using TinapayanRMS.UI.Controls;

namespace TinapayanRMS.Tests.Controls;

/// <summary>How many empty rows GridFillerRows puts under a grid's rows: whole rows only, never enough to scroll.</summary>
public class GridFillerRowsTests
{
    [Fact]
    public void A_short_table_is_filled_with_the_whole_rows_that_fit()
    {
        // A 280 px lines table: a 24 px header, two lines and the add-new row (96 px) leave room for 7 rows of 24 px.
        GridFillerRows.FillerRowCount(viewportHeight: 280, contentHeight: 96, rowHeight: 24).Should().Be(7);
    }

    [Fact]
    public void Rows_that_exactly_fill_the_grid_get_no_filler()
    {
        GridFillerRows.FillerRowCount(viewportHeight: 240, contentHeight: 240, rowHeight: 24).Should().Be(0);
    }

    [Fact]
    public void Rows_taller_than_the_grid_get_no_filler()
    {
        GridFillerRows.FillerRowCount(viewportHeight: 240, contentHeight: 600, rowHeight: 24).Should().Be(0);
    }

    [Fact]
    public void Space_for_less_than_a_row_is_left_empty()
    {
        GridFillerRows.FillerRowCount(viewportHeight: 250, contentHeight: 96, rowHeight: 24).Should().Be(6, "154 px holds six 24 px rows and 10 px of a seventh");
        GridFillerRows.FillerRowCount(viewportHeight: 110, contentHeight: 96, rowHeight: 24).Should().Be(0);
    }

    [Fact]
    public void A_hair_under_a_whole_row_from_fractional_layout_still_counts_it()
    {
        GridFillerRows.FillerRowCount(viewportHeight: 263.99999999, contentHeight: 72, rowHeight: 24).Should().Be(8);
    }

    [Fact]
    public void A_grid_without_a_height_to_fill_or_a_row_height_gets_no_filler()
    {
        GridFillerRows.FillerRowCount(double.PositiveInfinity, contentHeight: 96, rowHeight: 24).Should().Be(0);
        GridFillerRows.FillerRowCount(viewportHeight: 280, contentHeight: 96, rowHeight: 0).Should().Be(0);
        GridFillerRows.FillerRowCount(double.NaN, contentHeight: 96, rowHeight: 24).Should().Be(0);
    }
}
