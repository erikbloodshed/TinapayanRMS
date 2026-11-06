using System.Collections.ObjectModel;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// A lines table whose lines the screen replaces while its add row is open. The grid commits an open add row whenever
/// its collection changes, which adds to the collection inside its own CollectionChanged, so Products' Save would crash
/// with "Cannot change ObservableCollection during a CollectionChanged event" when its reload refills the form's
/// lines. The screen listens to the collection too, as ProductsViewModel does: with the grid as the only
/// listener, ObservableCollection lets the nested add through.
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class GridAddNewRowSourceChangeTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Test]
    public void Refilling_the_lines_under_an_open_add_row_drops_the_add_row()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            TypeIntoAddRow(grid, "Sugar");

            lines.Clear();
            lines.Add(new Line { Name = "Salt", Grams = 20m });
            OffscreenGrid.Pump();

            lines.Select(l => l.Name).Should().Equal("Salt");
            grid.View.IsAddingNew.Should().BeFalse();
        });
    }

    [Test]
    public void Adding_a_line_under_an_open_add_row_drops_the_add_row()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            TypeIntoAddRow(grid, "Sugar");

            lines.Add(new Line { Name = "Salt", Grams = 20m });
            OffscreenGrid.Pump();

            lines.Select(l => l.Name).Should().Equal("Flour", "Water", "Salt");
            grid.View.IsAddingNew.Should().BeFalse();
        });
    }

    [Test]
    public void A_line_typed_into_the_add_row_is_still_added_by_the_grid()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var added = TypeIntoAddRow(grid, "Sugar");

            GridEditCommit.Commit(grid);
            OffscreenGrid.Pump();

            lines.Should().HaveCount(3).And.Contain(added);
        });
    }

    /// <summary>Starts the add row in its Grams cell, as a tap there does, and gives its line a name.</summary>
    private static Line TypeIntoAddRow(SfDataGrid grid, string name)
    {
        grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex() + 1));
        grid.SelectionController.CurrentCellManager.BeginEdit();
        OffscreenGrid.Pump();

        grid.View.IsAddingNew.Should().BeTrue();
        var line = (Line)grid.View.CurrentAddItem;
        line.Name = name;
        return line;
    }

    /// <summary>A lines table with two lines and an add row, set up as the app's are, and a second listener on its lines.</summary>
    private static SfDataGrid LinesGrid()
    {
        var lines = new ObservableCollection<Line>
        {
            new() { Name = "Flour", Grams = 1000m },
            new() { Name = "Water", Grams = 650m },
        };
        lines.CollectionChanged += (_, _) => { };

        var grid = new SfDataGrid
        {
            ItemsSource = lines,
            AutoGenerateColumns = true,
            AllowEditing = true,
            AllowDeleting = true,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = AddNewRowPosition.Bottom,
            SelectionMode = GridSelectionMode.Single,
            Height = 200,
        };

        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridEditCommit.SetOnFocusLeave(grid, true);
        return grid;
    }
}
