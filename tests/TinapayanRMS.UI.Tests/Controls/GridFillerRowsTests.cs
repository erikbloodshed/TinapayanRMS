using AwesomeAssertions;
using TinapayanRMS.UI.Controls;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// How many empty rows GridFillerRows draws under a grid's rows: enough to reach its bottom edge, the last cut off by it
/// when only part fits, and none when the rows already reach it.
/// </summary>
public class GridFillerRowsTests
{
    [Fact]
    public void A_short_table_is_filled_with_the_rows_that_fit()
    {
        // A 264 px lines table: a 24 px header, a line and the add-new row (72 px) leave room for 8 rows of 24 px.
        GridFillerRows.FillerRowCount(viewportHeight: 264, contentHeight: 72, rowHeight: 24).Should().Be(8);
    }

    [Fact]
    public void Space_for_part_of_a_row_gets_a_row_the_bottom_edge_cuts_off()
    {
        GridFillerRows.FillerRowCount(viewportHeight: 250, contentHeight: 96, rowHeight: 24).Should().Be(7, "154 px holds six 24 px rows and 10 px of a seventh");
        GridFillerRows.FillerRowCount(viewportHeight: 110, contentHeight: 96, rowHeight: 24).Should().Be(1);
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
    public void A_hair_either_side_of_whole_rows_from_fractional_layout_counts_as_whole_rows()
    {
        GridFillerRows.FillerRowCount(viewportHeight: 263.99999999, contentHeight: 72, rowHeight: 24).Should().Be(8);
        GridFillerRows.FillerRowCount(viewportHeight: 264.00000001, contentHeight: 72, rowHeight: 24).Should().Be(8);
        GridFillerRows.FillerRowCount(viewportHeight: 240.00000001, contentHeight: 240, rowHeight: 24).Should().Be(0);
    }

    [Fact]
    public void A_grid_without_a_height_to_fill_or_a_row_height_gets_no_filler()
    {
        GridFillerRows.FillerRowCount(double.PositiveInfinity, contentHeight: 96, rowHeight: 24).Should().Be(0);
        GridFillerRows.FillerRowCount(viewportHeight: 280, contentHeight: 96, rowHeight: 0).Should().Be(0);
        GridFillerRows.FillerRowCount(double.NaN, contentHeight: 96, rowHeight: 24).Should().Be(0);
    }
}
