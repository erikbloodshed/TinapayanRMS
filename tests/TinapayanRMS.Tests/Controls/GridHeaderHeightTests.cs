using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.Tests.Fixtures;
using TinapayanRMS.UI.Controls;

namespace TinapayanRMS.Tests.Controls;

/// <summary>GridHeaderHeight: a grid's header row fits its text at the app's font size, where the theme's 24px cut off descenders.</summary>
[Collection(OffscreenGridCollection.Name)]
public class GridHeaderHeightTests
{
    public sealed class Line
    {
        public string Ingredient { get; set; } = string.Empty;
    }

    [Fact]
    public void At_the_apps_14px_the_header_row_grows_past_the_themes_24px()
    {
        // The theme's header cell: 3px padding above and below, a 1px bottom border.
        var padding = new Thickness(5, 3, 5, 3);
        var border = new Thickness(0, 0, 1, 1);

        GridHeaderHeight.HeightFor(new FontFamily("Segoe UI"), 14, padding, border).Should().Be(26, "19px of text, 6 of padding, 1 of border");
        GridHeaderHeight.HeightFor(new FontFamily("Segoe UI"), 12, padding, border).Should().Be(24, "the theme's own height already fits its 12px text");
    }

    [Fact]
    public void A_loaded_grid_header_is_tall_enough_for_its_text()
    {
        GridHeaderHeight.Register();

        OffscreenGrid.Run(() => Grid(), grid =>
        {
            var header = HeaderCells(grid).First(c => c.IsVisible);
            grid.HeaderRowHeight.Should().Be(GridHeaderHeight.HeightFor(header.FontFamily, header.FontSize, header.Padding, header.BorderThickness));

            // The header's text, descenders and all, fits inside its cell.
            var text = Descendants(header).OfType<TextBlock>().First(t => t.Text == "Ingredient");
            text.TranslatePoint(new Point(0, text.ActualHeight), header).Y.Should().BeLessThanOrEqualTo(header.ActualHeight);
        });
    }

    [Fact]
    public void A_grid_hidden_when_it_loads_is_fitted_when_first_shown()
    {
        GridHeaderHeight.Register();

        OffscreenGrid.Run(() => Grid(fontSize: 20, visibility: Visibility.Collapsed), grid =>
        {
            HeaderCells(grid).Should().BeEmpty("a collapsed grid has laid nothing out");

            grid.Visibility = Visibility.Visible;
            OffscreenGrid.Pump();

            var header = HeaderCells(grid).First(c => c.IsVisible);
            grid.HeaderRowHeight.Should().Be(GridHeaderHeight.HeightFor(header.FontFamily, header.FontSize, header.Padding, header.BorderThickness))
                .And.BeGreaterThan(24);
        });
    }

    [Fact]
    public void A_header_height_set_in_xaml_is_kept()
    {
        GridHeaderHeight.Register();

        OffscreenGrid.Run(() => Grid(headerRowHeight: 40), grid => grid.HeaderRowHeight.Should().Be(40));
    }

    /// <summary>A one-line grid whose header cells are <paramref name="fontSize"/> (the theme sets theirs apart from the grid's).</summary>
    private static SfDataGrid Grid(double fontSize = 14, Visibility visibility = Visibility.Visible, double? headerRowHeight = null)
    {
        var headerStyle = new Style(typeof(GridHeaderCellControl));
        headerStyle.Setters.Add(new Setter(Control.FontSizeProperty, fontSize));
        headerStyle.Setters.Add(new Setter(Control.FontFamilyProperty, new FontFamily("Segoe UI")));
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line> { new() { Ingredient = "Flour" } },
            AutoGenerateColumns = true,
            HeaderStyle = headerStyle,
            Visibility = visibility,
            Height = 200,
        };

        if (headerRowHeight is { } height)
        {
            grid.HeaderRowHeight = height;
        }

        return grid;
    }

    private static IEnumerable<GridHeaderCellControl> HeaderCells(DependencyObject grid) => Descendants(grid).OfType<GridHeaderCellControl>();

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
