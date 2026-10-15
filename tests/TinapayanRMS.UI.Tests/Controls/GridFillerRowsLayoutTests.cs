using System.Collections.ObjectModel;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridFillerRows draws its empty rows without adding any to the grid, and keeps them to the space under the grid's rows
/// in the same layout pass as the grid changes: no settle delay, and never a row over the grid's own.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridFillerRowsLayoutTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Fact]
    public void A_short_table_is_padded_with_drawn_rows_and_none_added_to_the_grid()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().Be(RowsThatFit(grid)).And.BeGreaterThan(0);
            grid.UnBoundRows.Should().BeEmpty();
        });
    }

    [Fact]
    public void Lines_added_or_removed_change_the_padding_in_the_same_layout_pass()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var before = OffscreenGrid.FillerLayer(grid).Layout.RowCount;

            lines.Add(new Line { Name = "Salt", Grams = 20m });
            lines.Add(new Line { Name = "Yeast", Grams = 10m });
            grid.UpdateLayout();

            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().Be(before - 2);

            lines.RemoveAt(lines.Count - 1);
            grid.UpdateLayout();

            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().Be(before - 1);
        });
    }

    [Fact]
    public void Rows_that_outgrow_the_grid_leave_no_padding()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            for (var i = 0; i < 20; i++)
            {
                lines.Add(new Line { Name = $"Line {i}", Grams = i });
            }

            grid.UpdateLayout();

            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().Be(0);
        });
    }

    [Fact]
    public void A_grid_resized_is_padded_to_its_new_height_at_once()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var before = OffscreenGrid.FillerLayer(grid).Layout.RowCount;

            grid.Height += 120;
            grid.UpdateLayout();
            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().Be(before + 5);

            grid.Height -= 240;
            grid.UpdateLayout();
            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().Be(RowsThatFit(grid)).And.BeLessThan(before);
        });
    }

    [Fact]
    public void Turned_off_it_leaves_the_grid_as_it_was()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            GridFillerRows.SetIsEnabled(grid, false);
            OffscreenGrid.Pump();

            var act = () => OffscreenGrid.FillerLayer(grid);

            act.Should().Throw<InvalidOperationException>();
            grid.UnBoundRows.Should().BeEmpty();
        });
    }

    private static int RowsThatFit(SfDataGrid grid)
    {
        var rows = (PixelScrollAxis)grid.GetVisualContainer().ScrollRows;
        return GridFillerRows.FillerRowCount(rows.RenderSize, rows.TotalExtent, grid.RowHeight);
    }

    /// <summary>A 300 px lines table with two lines and an add row, as the app's are.</summary>
    private static SfDataGrid LinesGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line>
            {
                new() { Name = "Flour", Grams = 1000m },
                new() { Name = "Water", Grams = 650m },
            },
            AutoGenerateColumns = true,
            AllowEditing = true,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = AddNewRowPosition.Bottom,
            ShowRowHeader = true,
            SelectionMode = GridSelectionMode.Single,
            Height = 300,
        };
        GridFillerRows.SetIsEnabled(grid, true);
        return grid;
    }
}
