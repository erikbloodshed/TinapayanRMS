using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Draws <see cref="GridFillerRows"/>' empty rows: the grid lines of the whole rows that fit under an SfDataGrid's last
/// row, each row's bottom line and each column's right-hand one, as the theme draws a cell's (a 1 px border on its right
/// and bottom, in the grid's own BorderBrush). Read from the decompiled Syncfusion 35.1.40:
/// <list type="bullet">
/// <item>It's put in the grid's template, in the root Grid that holds the ScrollViewer and its VisualContainer, over them
/// but never hit-tested. So it's drawn and clipped with the grid, under anything over the grid on the screen (an error
/// toast, the Vim hints), and clicks on it reach the grid, which has nothing there.</item>
/// <item>The rows' space is measured on the row scroll axis (render size against total extent), which counts the
/// header, group captions, the add row and a summary row as they're drawn, and the columns' edges come from the column
/// scroll axis's visible lines, so they follow column widths, the row header and horizontal scrolling.</item>
/// <item>It's worked out again after every layout pass while the grid is loaded, which compares a few numbers, and
/// redrawn only when they changed, by replacing its drawing: no layout follows, so lines never lag a resize.</item>
/// </list>
/// </summary>
public sealed class GridFillerLayer : FrameworkElement
{
    private readonly SfDataGrid _grid;
    private readonly DrawingGroup _drawing = new();
    private bool _watching;

    public GridFillerLayer(SfDataGrid grid)
    {
        _grid = grid;
        IsHitTestVisible = false;
        Focusable = false;
    }

    /// <summary>What it last drew: <see cref="FillerLayout.Empty"/> when no whole row fits.</summary>
    public FillerLayout Layout { get; private set; } = FillerLayout.Empty;

    public void StartWatching()
    {
        if (!_watching)
        {
            _grid.LayoutUpdated += OnLayoutUpdated;
            _watching = true;
        }

        Refresh();
    }

    public void StopWatching()
    {
        if (_watching)
        {
            _grid.LayoutUpdated -= OnLayoutUpdated;
            _watching = false;
        }
    }

    /// <summary>Stops drawing and leaves the grid's template.</summary>
    public void Remove()
    {
        StopWatching();
        (Parent as Panel)?.Children.Remove(this);
    }

    protected override void OnRender(DrawingContext drawingContext) => drawingContext.DrawDrawing(_drawing);

    private void OnLayoutUpdated(object? sender, EventArgs e) => Refresh();

    private void Refresh()
    {
        EnsureInTemplate();
        var layout = Measure();
        if (layout.Equals(Layout))
        {
            return;
        }

        Layout = layout;
        Redraw();
    }

    /// <summary>
    /// In the template's root Grid, across all its rows: the grid's template is applied when it's first loaded, and a new
    /// one (a theme change) leaves this in the old, so it's checked each time.
    /// </summary>
    private void EnsureInTemplate()
    {
        if (VisualTreeHelper.GetChildrenCount(_grid) == 0 || VisualTreeHelper.GetChild(_grid, 0) is not Grid root || ReferenceEquals(Parent, root))
        {
            return;
        }

        (Parent as Panel)?.Children.Remove(this);
        Grid.SetRowSpan(this, Math.Max(1, root.RowDefinitions.Count));
        Grid.SetColumnSpan(this, Math.Max(1, root.ColumnDefinitions.Count));
        root.Children.Add(this);
    }

    private FillerLayout Measure()
    {
        if (Parent is not Grid root
            || _grid.GetVisualContainer() is not { } container
            || container.ScrollRows is not PixelScrollAxis rows
            || container.ScrollColumns is not { } columns
            || !root.IsAncestorOf(container))
        {
            return FillerLayout.Empty;
        }

        var rowHeight = _grid.RowHeight;
        var rowCount = GridFillerRows.FillerRowCount(rows.RenderSize, rows.TotalExtent, rowHeight);
        if (rowCount == 0)
        {
            return FillerLayout.Empty;
        }

        // Each visible column where its cells are, and the part of it not scrolled under the row header or past the edge.
        var origin = container.TransformToVisual(this).Transform(default);
        List<FillerColumn> fillerColumns = [];
        foreach (var line in columns.GetVisibleLines())
        {
            if (line.ClippedSize > 0)
            {
                fillerColumns.Add(new FillerColumn(
                    origin.X + line.Origin, origin.X + line.Corner, origin.X + line.ClippedOrigin, origin.X + line.ClippedCorner));
            }
        }

        return new FillerLayout(
            new Rect(origin, container.RenderSize),
            origin.Y + rows.TotalExtent,
            rowHeight,
            rowCount,
            fillerColumns,
            _grid.BorderBrush,
            _grid.GridLinesVisibility);
    }

    /// <summary>
    /// Each empty cell's border as the theme's cell draws it: a Border with BorderThickness 0,0,1,1 (one side dropped for
    /// lines one way only), which is its outline less its inside, a strip down the right and one along the bottom that
    /// stops short of it. Its SnapsToDevicePixels snaps only the cell's own edges to device pixels, so the strips' inner
    /// edges fall 1 px inside them unsnapped, as wide as 1 px is at the monitor's scale (a pixel and a quarter at 125%).
    /// The strips never overlap, which matters: the theme's brush is see-through, and a crossing drawn twice is darker.
    /// </summary>
    private void Redraw()
    {
        using var context = _drawing.Open();
        var layout = Layout;
        if (layout.RowCount == 0 || layout.Brush is not { } brush || layout.Lines == GridLinesVisibility.None)
        {
            return;
        }

        var right = layout.Lines is GridLinesVisibility.Both or GridLinesVisibility.Vertical ? 1d : 0d;
        var bottom = layout.Lines is GridLinesVisibility.Both or GridLinesVisibility.Horizontal ? 1d : 0d;
        context.PushClip(new RectangleGeometry(layout.Viewport));
        foreach (var column in layout.Columns)
        {
            // Only a column scrolled partly under the row header is cut to what's shown of it, and only on that side; the
            // grid's edges are the viewport's clip. A clip anywhere else would shave a line where it doesn't fall on a
            // pixel, as cells aren't, and a second clip on the viewport's edge would shave it twice.
            var clipLeft = column.VisibleLeft > Math.Max(column.Left, layout.Viewport.Left) ? column.VisibleLeft : column.Left - 1;
            var clipRight = column.VisibleRight < Math.Min(column.Right, layout.Viewport.Right) ? column.VisibleRight : column.Right + 1;
            var clipped = clipLeft >= column.Left || clipRight <= column.Right;
            if (clipped)
            {
                context.PushClip(new RectangleGeometry(new Rect(clipLeft, layout.Viewport.Top, Math.Max(0, clipRight - clipLeft), layout.Viewport.Height)));
            }

            for (var row = 0; row < layout.RowCount; row++)
            {
                var top = layout.Top + (row * layout.RowHeight);
                var cell = new Rect(column.Left, top, column.Right - column.Left, layout.RowHeight);

                // The cell's own edges, as SnapsToDevicePixels gives a cell (UIElement's snapping guidelines are its bounds).
                context.PushGuidelineSet(new GuidelineSet([cell.Left, cell.Right], [cell.Top, cell.Bottom]));
                if (right > 0)
                {
                    context.DrawRectangle(brush, null, new Rect(cell.Right - right, cell.Top, right, cell.Height));
                }

                if (bottom > 0)
                {
                    context.DrawRectangle(brush, null, new Rect(cell.Left, cell.Bottom - bottom, Math.Max(0, cell.Width - right), bottom));
                }

                context.Pop();
            }

            if (clipped)
            {
                context.Pop();
            }
        }

        context.Pop();
    }
}

/// <summary>
/// A column as <see cref="GridFillerLayer"/> draws its empty cells: where its cells are (Left, Right), which runs past
/// what's shown when it's scrolled partly out of sight, and the part shown (VisibleLeft, VisibleRight).
/// </summary>
public readonly record struct FillerColumn(double Left, double Right, double VisibleLeft, double VisibleRight);

/// <summary>
/// Where <see cref="GridFillerLayer"/> draws its rows, in its own coordinates: the grid's rows' area (Viewport), the top
/// of the first empty row, how many fit and the columns they're divided into.
/// </summary>
public sealed record FillerLayout(
    Rect Viewport,
    double Top,
    double RowHeight,
    int RowCount,
    IReadOnlyList<FillerColumn> Columns,
    Brush? Brush,
    GridLinesVisibility Lines)
{
    public static FillerLayout Empty { get; } = new(Rect.Empty, 0, 0, 0, [], null, GridLinesVisibility.None);

    public bool Equals(FillerLayout? other) =>
        other is not null
        && Viewport == other.Viewport
        && Top == other.Top
        && RowHeight == other.RowHeight
        && RowCount == other.RowCount
        && Columns.SequenceEqual(other.Columns)
        && ReferenceEquals(Brush, other.Brush)
        && Lines == other.Lines;

    public override int GetHashCode() => HashCode.Combine(Viewport, Top, RowCount, Columns.Count);
}
