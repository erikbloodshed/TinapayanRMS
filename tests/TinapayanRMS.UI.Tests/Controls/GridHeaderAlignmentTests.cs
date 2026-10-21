using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridHeaderAlignment: a grid's headings sit at the bottom of its two-line header row, as the Price Simulator's Products
/// table has them, with the theme's look kept. The grid is themed as the app's are, with the app's wrapping headings
/// (Lines 2, a HeaderTemplate that wraps), one heading of one line beside one of two.
/// </summary>
public class GridHeaderAlignmentTests
{
    public sealed record Row(string Name, decimal Price);

    [Fact]
    public void One_and_two_line_headings_end_at_the_bottom_of_the_header_row_with_the_themes_look() => StaThread.RunThemed(() =>
    {
        var (window, centred) = Show(bottom: false);
        Color themeBorder;
        try
        {
            themeBorder = ((SolidColorBrush)Heading(centred, "Product").Cell.BorderBrush).Color;
        }
        finally
        {
            window.Close();
        }

        (window, var grid) = Show(bottom: true);
        try
        {
            var name = Heading(grid, "Product");
            var price = Heading(grid, "Simulated Suggested Price");
            var rowBottom = name.Cell.ActualHeight - name.Cell.Padding.Bottom - name.Cell.BorderThickness.Bottom;

            price.Text.LineCount().Should().Be(2, "the long heading wraps");
            name.Bottom.Should().BeApproximately(price.Bottom, 0.5, "a one-line heading ends where a two-line one does");
            name.Bottom.Should().BeApproximately(rowBottom, 1.5, "at the bottom of the header row, not its middle");
            name.FunnelBottom.Should().BeApproximately(rowBottom, 1.5, "the filter funnel sits down beside its heading");
            ((SolidColorBrush)name.Cell.BorderBrush).Color.Should().Be(themeBorder, "the theme's own style is kept");
        }
        finally
        {
            window.Close();
        }
    });

    private static (Window Window, SfDataGrid Grid) Show(bool bottom)
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new[] { new Row("Pandesal", 5m) },
            AutoGenerateColumns = false,
            AllowSorting = true,
            Height = 200,
            HeaderTemplate = (DataTemplate)XamlReader.Parse(
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding}' TextWrapping='Wrap' TextAlignment='Right' /></DataTemplate>"),
            Columns =
            {
                new GridTextColumn { MappingName = nameof(Row.Name), HeaderText = "Product", AllowFiltering = true, Width = 160 },
                new GridNumericColumn { MappingName = nameof(Row.Price), HeaderText = "Simulated Suggested Price", Width = 150 },
            },
        };
        GridHeaderHeight.SetLines(grid, 2);
        GridHeaderAlignment.SetIsBottom(grid, bottom);

        var window = new Window
        {
            Content = grid,
            Width = 400,
            Height = 300,
            Left = -20000,
            Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        SfSkinManager.SetTheme(window, new Theme("Windows11Light"));
        window.Show();
        OffscreenGrid.Pump();
        return (window, grid);
    }

    private sealed record HeadingLayout(GridHeaderCellControl Cell, TextBlock Text, double Bottom, double FunnelBottom);

    /// <summary>The header cell headed <paramref name="text"/>, and where its heading and funnel end, from the cell's top.</summary>
    private static HeadingLayout Heading(SfDataGrid grid, string text)
    {
        var cell = Descendants(grid).OfType<GridHeaderCellControl>().Single(c => Equals(c.Column?.HeaderText, text));
        var heading = Descendants(cell).OfType<TextBlock>().Single(t => t.Text == text);
        var funnel = Descendants(cell).OfType<ToggleButton>().FirstOrDefault(b => b.IsVisible);
        return new(
            cell,
            heading,
            heading.TranslatePoint(new Point(0, heading.ActualHeight), cell).Y,
            funnel is null ? double.NaN : funnel.TranslatePoint(new Point(0, funnel.ActualHeight), cell).Y);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}

internal static class TextBlockLines
{
    /// <summary>How many lines <paramref name="text"/> is laid out in, from its height and its font's line spacing.</summary>
    public static int LineCount(this TextBlock text) =>
        (int)Math.Round(text.ActualHeight / (text.FontSize * text.FontFamily.LineSpacing));
}
