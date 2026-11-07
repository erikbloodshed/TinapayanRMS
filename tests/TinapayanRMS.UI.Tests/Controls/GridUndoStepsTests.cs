using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// GridUndoSteps' pending edit in a real lines table set up as the app's are (UsageFormPanel.xaml), beside a Save button
/// offered only while an edit is pending or the lines have changed, as the screens offer theirs. A line typed on the add
/// row and saved with a click on Save, the add row still open, is added (Products' add-ins).
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class GridUndoStepsTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    /// <summary>A screen's recorder, reduced to what enables its Save: an edit pending, or lines other than the two it started with.</summary>
    private sealed class SaveOffer(ObservableCollection<Line> lines, Button save) : IUndoRecorder
    {
        private bool _isEditPending;

        public int FormVersion => 0;

        public bool IsEditPending
        {
            get => _isEditPending;
            set
            {
                _isEditPending = value;
                Refresh();
            }
        }

        public void BeginUndoStep()
        {
        }

        public void EndUndoStep() => Refresh();

        public void Refresh() => save.IsEnabled = _isEditPending || lines.Count != 2;
    }

    [Test]
    public void Clicking_Save_with_a_line_typed_on_the_add_row_adds_the_line_and_keeps_Save_offered()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var (lines, save) = AddSaveButton(grid);
            var typed = TypeIntoAddRow(grid);

            // The press on Save takes the cursor, which ends the cell's edit on the way.
            save.Focus();
            OffscreenGrid.Pump();

            lines.Should().HaveCount(3).And.Contain(typed);
            save.IsEnabled.Should().BeTrue("the click that took the cursor still has to land on it");
            save.IsKeyboardFocused.Should().BeTrue();
        });
    }

    [Test]
    public void An_add_row_dropped_after_its_cell_was_left_is_no_longer_pending()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var (_, save) = AddSaveButton(grid);
            TypeIntoAddRow(grid);
            grid.SelectionController.CurrentCellManager.EndEdit();
            save.IsEnabled.Should().BeTrue("the line typed is still on its way to the lines");

            grid.GetAddNewRowController().CancelAddNew();
            OffscreenGrid.Pump();

            save.IsEnabled.Should().BeFalse("nothing reached the lines");
        });
    }

    [Test]
    public void An_add_row_left_untouched_is_not_pending()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var (_, save) = AddSaveButton(grid);
            grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex()));
            grid.SelectionController.CurrentCellManager.BeginEdit();
            OffscreenGrid.Pump();

            grid.SelectionController.CurrentCellManager.EndEdit();

            save.IsEnabled.Should().BeFalse();
        });
    }

    /// <summary>Puts a Save button above <paramref name="grid"/> in its window, offered as a screen offers it, and gives the window the cursor.</summary>
    private static (ObservableCollection<Line> Lines, Button Save) AddSaveButton(SfDataGrid grid)
    {
        var lines = (ObservableCollection<Line>)grid.ItemsSource;
        var save = new Button { Content = "Save", IsEnabled = false };
        var window = Window.GetWindow(grid)!;
        window.Content = null;
        window.Content = new StackPanel { Children = { save, grid } };
        window.Activate();
        OffscreenGrid.Pump();

        GridUndoSteps.SetRecorder(grid, new SaveOffer(lines, save));
        grid.Focus();
        return (lines, save);
    }

    /// <summary>Opens the add row's Grams cell, as a tap on it does, and gives its line a name.</summary>
    private static Line TypeIntoAddRow(SfDataGrid grid)
    {
        grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex() + 1));
        grid.SelectionController.CurrentCellManager.BeginEdit();
        OffscreenGrid.Pump();

        grid.View.IsAddingNew.Should().BeTrue();
        Keyboard.FocusedElement.Should().BeAssignableTo<DependencyObject>().Which.Should().NotBeSameAs(grid, "the cell's editor has the cursor");
        var line = (Line)grid.View.CurrentAddItem;
        line.Name = "Sugar";
        return line;
    }

    /// <summary>A 200 px lines table with two lines and an add row, with the app's add-row and commit behaviours.</summary>
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
