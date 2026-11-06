using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// CheckboxFilterFill: a grid filter pop-up's list of values fills the pop-up. The Windows11Light theme lays the list out
/// in a column fixed at 188px, whatever the pop-up's width, so Reports' Product filter cut "Special Ensaymada Small Zero
/// Topping" off. The pop-up isn't opened: one opened from an off-screen grid is moved onto a real screen. A data grid sits
/// beside the filter control, since SfSkinManager themes the grid's controls only once it meets a grid. The first check
/// fails once Syncfusion's theme stops fixing the width, when the fix stops mattering.
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class CheckboxFilterFillTests
{
    [Test]
    public void The_list_of_values_fills_the_pop_up_where_the_theme_fixes_it_at_188px() => StaThread.RunThemed(() =>
    {
        var filterControl = new GridFilterControl();
        var window = new Window
        {
            Content = new StackPanel { Children = { new SfDataGrid { Height = 100 }, filterControl } },
            Width = 400,
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

            ValuesColumn(filterControl).Width.Should().Be(new GridLength(188), "the theme's own width, which the fix is for");

            CheckboxFilterFill.Fill(filterControl);

            ValuesColumn(filterControl).Width.Should().Be(new GridLength(1, GridUnitType.Star));
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>The column of the filter control's list (CheckboxFilterControl) that its search box and values are in.</summary>
    private static ColumnDefinition ValuesColumn(GridFilterControl filterControl)
    {
        filterControl.ApplyTemplate();
        var list = (CheckboxFilterControl)filterControl.Template.FindName("PART_CheckboxFilterControl", filterControl);
        list.ApplyTemplate();
        return ((Grid)VisualTreeHelper.GetChild(list, 0)).ColumnDefinitions[^1];
    }
}
