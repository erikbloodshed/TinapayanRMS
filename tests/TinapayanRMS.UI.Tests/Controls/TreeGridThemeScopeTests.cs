using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.TreeGrid;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// Syncfusion's Windows11Light dictionary for SfTreeGrid (34.2.9) merges in SfGrid's unthemed Control/Themes/Generic.xaml,
/// whose implicit GridCell, header cell and row styles are the very ones its SfDataGrid dictionary themes. SfSkinManager
/// adds each control's dictionary to the window's in the order it meets them, so a tree grid inheriting the window's theme
/// after the data grids leaves every data grid in the app unthemed: black grid lines, a bold add-row +, until a cell is
/// edited. ScalingCard's tree
/// grid is themed on its own (SfSkinManager.Theme set on it), which keeps its dictionaries in its own resources. The
/// second half fails once Syncfusion's dictionary stops carrying the generic styles, when the local theme stops mattering.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class TreeGridThemeScopeTests
{
    private sealed record Row(string Name);

    [Fact]
    public void A_tree_grid_themed_on_its_own_leaves_the_data_grids_themed_and_one_inheriting_the_theme_doesnt() => StaThread.RunThemed(() =>
    {
        var themed = DataGridCellBorder(treeGrid: null);

        DataGridCellBorder(TreeGrid(themedOnItsOwn: true)).Should().Be(themed, "its dictionaries stay in its own resources");
        DataGridCellBorder(TreeGrid(themedOnItsOwn: false)).Should().NotBe(themed, "the generic cell style it brings wins in the window");
    });

    /// <summary>
    /// The theme's cell style is one of those the generic ones outrank, so a tree grid's cells don't take the app's 14px
    /// body text (App.ThemeSettings) as its header cells do: they inherit the grid's own FontSize, which is 12. ScalingCard
    /// sets its grid's FontSize to 14.
    /// </summary>
    [Fact]
    public void A_tree_grid_themed_on_its_own_sizes_its_cells_text_by_its_own_font_size() => StaThread.RunThemed(() =>
    {
        var (cell, header) = CellFontSizes(gridFontSize: null);
        cell.Should().Be(12, "only the header cells take the theme's text size");
        header.Should().Be(14);

        (cell, header) = CellFontSizes(gridFontSize: 14);
        cell.Should().Be(14);
        header.Should().Be(14);
    });

    /// <summary>The font sizes of a tree grid's first cell and first header cell, themed on its own in a themed window.</summary>
    private static (double Cell, double Header) CellFontSizes(double? gridFontSize)
    {
        var treeGrid = TreeGrid(themedOnItsOwn: true);
        if (gridFontSize is { } size)
        {
            treeGrid.FontSize = size;
        }

        var window = new Window
        {
            Content = treeGrid,
            Width = 600,
            Height = 400,
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

            return (Descendants(treeGrid).OfType<TreeGridCell>().First().FontSize,
                    Descendants(treeGrid).OfType<TreeGridHeaderCell>().First().FontSize);
        }
        finally
        {
            window.Close();
        }
    }

    private static SfTreeGrid TreeGrid(bool themedOnItsOwn)
    {
        var treeGrid = new SfTreeGrid { ItemsSource = new[] { new Row("Dough") }, Height = 120 };
        if (themedOnItsOwn)
        {
            SfSkinManager.SetTheme(treeGrid, new Theme("Windows11Light"));
        }

        return treeGrid;
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

    /// <summary>The border colour of a data grid's first cell, in a themed window with <paramref name="treeGrid"/> after it.</summary>
    private static Color DataGridCellBorder(SfTreeGrid? treeGrid)
    {
        var dataGrid = new SfDataGrid { ItemsSource = new[] { new Row("Flour") }, Height = 120 };
        var panel = new StackPanel();
        panel.Children.Add(dataGrid);
        if (treeGrid is not null)
        {
            panel.Children.Add(treeGrid);
        }

        var window = new Window
        {
            Content = panel,
            Width = 600,
            Height = 400,
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

            var cell = Descendants(dataGrid).OfType<GridCell>().First();
            return ((SolidColorBrush)cell.BorderBrush).Color;
        }
        finally
        {
            window.Close();
        }
    }
}
