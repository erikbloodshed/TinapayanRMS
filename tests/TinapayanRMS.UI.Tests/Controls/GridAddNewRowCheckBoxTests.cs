using System.Collections.ObjectModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// The Base tick in a lines table's add row (GridAddNewRow.HideCheckBoxes, AddRowHiddenCheckBoxRenderer): blank while the
/// row holds no line, there to tick once one is being typed into it, and the tick goes in with the line. Hidden until the
/// line is added, the Base of a line just typed couldn't be ticked on a new sub-assembly or product section.
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class GridAddNewRowCheckBoxTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal? Grams { get; set; }

        public bool IsFormulaBase { get; set; }
    }

    [Test]
    public void The_add_row_shows_no_tick_until_a_line_is_typed_into_it()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            AddRowBox(grid).IsVisible.Should().BeFalse();

            StartAddRow(grid).Name = "Bread Flour";
            OffscreenGrid.Pump();

            AddRowBox(grid).IsVisible.Should().BeTrue();
        });
    }

    [Test]
    public void A_tick_on_a_line_being_typed_is_added_with_it()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var typed = StartAddRow(grid);
            typed.Name = "Bread Flour";
            typed.Grams = 1000m;
            OffscreenGrid.Pump();

            // A hidden box takes no mouse click, though one made in code would still reach it.
            AddRowBox(grid).IsVisible.Should().BeTrue();
            Click(AddRowBox(grid));
            GridEditCommit.Commit(grid);
            OffscreenGrid.Pump();

            lines.Should().HaveCount(2).And.Contain(typed);
            typed.IsFormulaBase.Should().BeTrue();
            AddRowBox(grid).IsVisible.Should().BeFalse("the next add row holds no line yet");
        });
    }

    [Test]
    public void The_lines_keep_their_ticks_shown()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var typed = StartAddRow(grid);
            typed.Name = "Water";
            typed.Grams = 650m;
            GridEditCommit.Commit(grid);
            OffscreenGrid.Pump();

            var boxes = Descendants(grid).OfType<CheckBox>().Where(b => b.DataContext is Line && !InAddRow(b)).ToList();
            boxes.Should().HaveCount(2).And.OnlyContain(b => b.IsVisible);

            Click(boxes.Single(b => b.DataContext == typed));
            typed.IsFormulaBase.Should().BeTrue("a line added through the add row ticks like any other");
        });
    }

    /// <summary>Opens the add row's Grams cell and returns its line.</summary>
    private static Line StartAddRow(SfDataGrid grid)
    {
        grid.Focus();
        grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex() + 1));
        grid.SelectionController.CurrentCellManager.BeginEdit();
        OffscreenGrid.Pump();
        return (Line)grid.View.CurrentAddItem;
    }

    /// <summary>Clicks <paramref name="box"/> as the mouse does: ToggleButton's OnClick ticks it and raises Click, which the grid's renderer handles.</summary>
    private static void Click(CheckBox box)
    {
        typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(box, null);
        OffscreenGrid.Pump();
    }

    private static CheckBox AddRowBox(SfDataGrid grid) => Descendants(grid).OfType<CheckBox>().Single(InAddRow);

    private static bool InAddRow(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent is not null; parent = VisualTreeHelper.GetParent(parent))
        {
            if (parent is AddNewRowControl)
            {
                return true;
            }
        }

        return false;
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

    /// <summary>One line and an add row, with the lines tables' Base column and add-row behaviours.</summary>
    private static SfDataGrid LinesGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line> { new() { Name = "All Purpose Flour", Grams = 500m } },
            AutoGenerateColumns = false,
            AllowEditing = true,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = AddNewRowPosition.Bottom,
            SelectionMode = GridSelectionMode.Single,
            ShowRowHeader = true,
            Height = 300,
        };
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Line.Name) });
        grid.Columns.Add(new NumericColumn { MappingName = nameof(Line.Grams), AllowNullValue = true });
        grid.Columns.Add(new GridCheckBoxColumn { MappingName = nameof(Line.IsFormulaBase) });
        GridAddNewRow.SetHideCheckBoxes(grid, true);
        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridAddNewRow.SetItemFactory(grid, () => new Line());
        GridEditCommit.SetOnFocusLeave(grid, true);
        return grid;
    }
}
