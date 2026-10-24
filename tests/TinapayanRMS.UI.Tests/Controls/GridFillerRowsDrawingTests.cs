using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AwesomeAssertions;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridFillerRows' drawn rows (GridFillerLayer) look like the grid's own empty rows: a themed grid padded with them is
/// rendered beside the same grid holding that many blank records, the last cut off by the bottom edge in both, and the
/// two must match pixel for pixel, at 100% and at the 125% the dev machine's primary monitor runs at, with and without a
/// row header, scrolled sideways with the columns passing under the row header, and under an add row, as the lines tables
/// the app types into are padded. A grid can't hold records below its add row, so there the blank one holds a blank
/// record in the add row's place, and only what's under the add row is compared.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridFillerRowsDrawingTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public string Grams { get; set; } = string.Empty;

        public string Cost { get; set; } = string.Empty;
    }

    /// <summary>
    /// How far a pixel may be off, per channel out of 255. Rendered at 125%, the one pixel column where the last column's
    /// line meets the grid's right edge comes out 2 levels apart, as the container's clip and the layer's fall on it a
    /// little differently. A crossing drawn twice is 13 levels darker, and a line a quarter-pixel out at least 5.
    /// </summary>
    private const int Tolerance = 2;

    /// <summary>
    /// How far a pixel may be off in the one pixel row where the grid's bottom edge cuts the last row partway across it.
    /// The grid cuts its own last row with a clip on that row, and the layer with one in its drawing, and the two cover
    /// that pixel a little differently: 3 or 4 levels apart where a line crosses it at 125%. The lines above it, checked
    /// at <see cref="Tolerance"/>, are where a line out of place would show.
    /// </summary>
    private const int CutRowTolerance = 4;

    [Theory]
    [InlineData(false, 96, false, false, false)]
    [InlineData(true, 96, false, false, false)]
    [InlineData(false, 120, false, false, false)]
    [InlineData(true, 120, false, false, false)]
    [InlineData(true, 96, true, false, false)]
    [InlineData(true, 120, true, false, false)]
    [InlineData(true, 96, false, true, false)]
    [InlineData(true, 120, false, true, false)]
    [InlineData(true, 96, false, false, true)]
    [InlineData(true, 120, false, false, true)]
    [InlineData(true, 120, true, false, true)]
    public void The_drawn_rows_match_blank_records(bool showRowHeader, int dpi, bool scrolled, bool resized, bool addRow) => StaThread.RunThemed(() =>
    {
        var padded = LinesGrid(showRowHeader, scrolled, blankRecords: 0, addRow);
        var host = new StackPanel { Orientation = Orientation.Horizontal, Children = { padded } };
        var window = new Window
        {
            Content = host,
            Width = 900,
            Height = 500,
            Left = -20000,
            Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            SfSkinManager.SetTheme(window, new Theme("Windows11Light"));
            window.Show();
            OffscreenGrid.Pump();
            var layout = OffscreenGrid.FillerLayer(padded).Layout;
            var rowCount = layout.RowCount;
            rowCount.Should().BeGreaterThan(0, "a 300 px grid with two lines has room for more");
            (layout.Top + (rowCount * layout.RowHeight)).Should().BeGreaterThan(layout.Viewport.Bottom + 1, "the last row is cut off by the grid's bottom edge");

            var blank = LinesGrid(showRowHeader, scrolled, addRow ? rowCount + 1 : rowCount, addRow: false);
            var fromRow = addRow ? UnderAddRow(padded, dpi) : 0;
            host.Children.Add(blank);
            OffscreenGrid.Pump();
            if (scrolled)
            {
                padded.GetVisualContainer().SetHorizontalOffset(60);
                blank.GetVisualContainer().SetHorizontalOffset(60);
                OffscreenGrid.Pump();
            }

            // A column dragged wider by its header's edge (AllowResizingColumns). While the drag goes on, the grid resizes
            // only the column's line on its scroll axis (GridColumnResizingController: ScrollColumns.SetLineResize); its
            // Width is set when the mouse is let go. The drawn lines must follow both, in the same layout pass.
            if (resized)
            {
                var before = OffscreenGrid.FillerLayer(padded).Layout.Columns;
                var nameLine = padded.ResolveToScrollColumnIndex(0);
                padded.GetVisualContainer().ScrollColumns.SetLineResize(nameLine, 180.75);
                blank.GetVisualContainer().ScrollColumns.SetLineResize(nameLine, 180.75);
                padded.UpdateLayout();
                OffscreenGrid.FillerLayer(padded).Layout.Columns.Should().NotEqual(before, "the drawn lines follow the column as it's dragged");
                OffscreenGrid.Pump();
                var dragging = Differences(Render(padded, dpi), Render(blank, dpi), fromRow, CutRow(padded, dpi));
                dragging.Should().BeEmpty("mid-drag, these pixels differ: {0}", string.Join("; ", dragging));

                foreach (var grid in new[] { padded, blank })
                {
                    grid.GetVisualContainer().ScrollColumns.ResetLineResize();
                    grid.Columns[0].Width = 181;
                }

                OffscreenGrid.Pump();
            }

            OffscreenGrid.FillerLayer(padded).Layout.RowCount.Should().Be(rowCount);
            var differences = Differences(Render(padded, dpi), Render(blank, dpi), fromRow, CutRow(padded, dpi));
            differences.Should().BeEmpty("the drawn rows should look like blank records, and these pixels don't: {0}", string.Join("; ", differences));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>
    /// A 300 px lines table as the app's are, with two lines and <paramref name="blankRecords"/> empty ones under them, an
    /// add row under those when <paramref name="addRow"/>, and GridFillerRows on when it has no blank records. Scrolled,
    /// its Name column is too wide for it, so it scrolls sideways.
    /// </summary>
    private static SfDataGrid LinesGrid(bool showRowHeader, bool scrolled, int blankRecords, bool addRow)
    {
        var lines = new ObservableCollection<Line>
        {
            new() { Name = "Flour", Grams = "1,000.0", Cost = "62.00" },
            new() { Name = "Water", Grams = "650.0", Cost = "0.00" },
        };
        for (var i = 0; i < blankRecords; i++)
        {
            lines.Add(new Line());
        }

        var grid = new SfDataGrid
        {
            ItemsSource = lines,
            AutoGenerateColumns = false,
            AllowEditing = addRow,
            AddNewRowPosition = addRow ? AddNewRowPosition.Bottom : AddNewRowPosition.None,
            GridLinesVisibility = GridLinesVisibility.Both,
            ShowRowHeader = showRowHeader,
            AllowRowHoverHighlighting = false,
            Width = 377.5,
            Height = 300,
            VerticalAlignment = VerticalAlignment.Top,
            ColumnSizer = GridLengthUnitType.AutoLastColumnFill,
        };
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Line.Name), Width = scrolled ? 300.25 : 140.25 });
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Line.Grams), Width = 90 });
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Line.Cost) });
        GridFillerRows.SetIsEnabled(grid, blankRecords == 0);

        // The blank records run past the bottom edge as the drawn rows do, and a scroll bar for them would narrow the grid.
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Hidden);
        return grid;
    }

    /// <summary><paramref name="element"/> as drawn at <paramref name="dpi"/>.</summary>
    private static RenderTargetBitmap Render(FrameworkElement element, int dpi)
    {
        var scale = dpi / 96d;
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(element.ActualWidth * scale), (int)Math.Ceiling(element.ActualHeight * scale), dpi, dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new VisualBrush(element), null, new Rect(0, 0, element.ActualWidth, element.ActualHeight));
        }

        bitmap.Render(visual);
        return bitmap;
    }

    /// <summary>
    /// The pixel row at <paramref name="dpi"/> where <paramref name="padded"/>'s bottom edge cuts its last drawn row
    /// partway across, or -1 when the edge falls between pixel rows.
    /// </summary>
    private static int CutRow(SfDataGrid padded, int dpi)
    {
        var layer = OffscreenGrid.FillerLayer(padded);
        var bottom = layer.TranslatePoint(new Point(0, layer.Layout.Viewport.Bottom), padded).Y * dpi / 96d;
        return Math.Abs(bottom - Math.Round(bottom)) < 1e-6 ? -1 : (int)Math.Floor(bottom);
    }

    /// <summary>
    /// The first pixel row at <paramref name="dpi"/> wholly under <paramref name="padded"/>'s add row, where its first
    /// drawn row starts. The add row's own bottom line ends there.
    /// </summary>
    private static int UnderAddRow(SfDataGrid padded, int dpi)
    {
        var layer = OffscreenGrid.FillerLayer(padded);
        var top = layer.TranslatePoint(new Point(0, layer.Layout.Top), padded).Y * dpi / 96d;
        return (int)Math.Ceiling(top - 1e-6);
    }

    /// <summary>
    /// The first 20 pixels from pixel row <paramref name="fromRow"/> down further apart than <see cref="Tolerance"/>
    /// (<see cref="CutRowTolerance"/> in <paramref name="cutRow"/>), as "x,y padded/blank" in ARGB.
    /// </summary>
    private static List<string> Differences(BitmapSource padded, BitmapSource blank, int fromRow, int cutRow)
    {
        var (a, b) = (Pixels(padded), Pixels(blank));
        List<string> differences = [];
        for (var i = fromRow * padded.PixelWidth * 4; i < a.Length && differences.Count < 20; i += 4)
        {
            var pixel = i / 4;
            var tolerance = pixel / padded.PixelWidth == cutRow ? CutRowTolerance : Tolerance;
            if (Enumerable.Range(i, 4).Any(c => Math.Abs(a[c] - b[c]) > tolerance))
            {
                differences.Add($"{pixel % padded.PixelWidth},{pixel / padded.PixelWidth} {BitConverter.ToUInt32(a, i):X8}/{BitConverter.ToUInt32(b, i):X8}");
            }
        }

        return differences;
    }

    private static byte[] Pixels(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
