using System.Collections.ObjectModel;
using System.Windows;
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
/// GridRowInserting in a real lines table set up as the app's are (SubAssembliesView.xaml, UsageFormPanel.xaml): a blank
/// line put in above the current one, as one undo step, with its first column opened for typing.
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class GridRowInsertingTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    /// <summary>Counts the steps the grid opens and closes, and how many lines each one saw them close on.</summary>
    private sealed class StepCounter(ObservableCollection<Line> lines) : IUndoRecorder
    {
        public int Begun { get; private set; }

        public int FormVersion => 0;

        public List<int> LineCountsAtEnd { get; } = [];

        public bool IsEditPending { get; set; }

        public void BeginUndoStep() => Begun++;

        public void EndUndoStep() => LineCountsAtEnd.Add(lines.Count);
    }

    [Test]
    public void A_line_goes_in_above_the_current_one_and_its_first_column_opens_for_typing()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[1]), grid.GetFirstColumnIndex() + 1));

            GridRowInserting.InsertAbove(grid);
            OffscreenGrid.Pump();

            lines.Should().HaveCount(4);
            lines[1].Name.Should().BeEmpty("the new line is a blank one, from the grid's ItemFactory");
            lines[2].Name.Should().Be("Line 2");
            grid.SelectedItem.Should().BeSameAs(lines[1]);
            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex
                .Should().Be(new RowColumnIndex(grid.ResolveToRowIndex(lines[1]), grid.GetFirstColumnIndex()));
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeTrue();
        });
    }

    [Test]
    public void From_the_add_row_the_line_goes_in_at_the_bottom()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex()));

            GridRowInserting.InsertAbove(grid);
            OffscreenGrid.Pump();

            lines.Should().HaveCount(4);
            lines[^1].Name.Should().BeEmpty();
            grid.View.IsAddingNew.Should().BeFalse("the add row left untouched is dropped, not added as a line too");
        });
    }

    [Test]
    public void The_insert_is_one_undo_step()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var counter = new StepCounter(lines);
            GridUndoSteps.SetRecorder(grid, counter);
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[0]), grid.GetFirstColumnIndex()));

            GridRowInserting.InsertAbove(grid);

            // The second step begun is the new line's cell opening, still open, which settles into nothing if it's left alone.
            counter.Begun.Should().Be(2);
            counter.LineCountsAtEnd.Should().Equal([4], "the insert's own step closed on the line put in");
        });
    }

    [Test]
    public void The_Insert_key_puts_a_line_in_while_no_cell_is_being_typed_into()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[2]), grid.GetFirstColumnIndex()));

            PressInsert(grid);

            lines.Should().HaveCount(4);
            lines[2].Name.Should().BeEmpty();
        });
    }

    [Test]
    public void A_grid_whose_lines_cant_be_changed_right_now_is_left_alone()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.AllowEditing = false;

            GridRowInserting.CanInsert(grid).Should().BeFalse();
            GridRowInserting.InsertLine.CanExecute(null, grid).Should().BeFalse();
            GridRowInserting.InsertAbove(grid);

            lines.Should().HaveCount(3);
        });
    }

    [Test]
    public void A_letter_typed_straight_after_the_insert_goes_into_the_new_line_not_the_one_below_it()
    {
        // The key is queued at Input priority, ahead of anything the insert put off until later: a fast typist, or Insert
        // and a letter pressed together.
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[1]), grid.GetFirstColumnIndex()));

            GridRowInserting.InsertAbove(grid);
            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.RowIndex
                .Should().Be(grid.ResolveToRowIndex(lines[1]), "the cursor is on the new line before its cell has opened");
            TypeLetter(grid, 'x');
            OffscreenGrid.Pump();

            lines.Should().HaveCount(4);
            lines[2].Name.Should().Be("Line 2", "the line the cursor was on is left alone");
            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.RowIndex.Should().Be(grid.ResolveToRowIndex(lines[1]));
        });
    }

    [Test]
    public void Insert_held_down_puts_in_one_line_whose_cell_takes_the_presses_after_it()
    {
        // Every press queued before any of them is handled, as a held key's repeats are.
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[1]), grid.GetFirstColumnIndex()));

            for (var i = 0; i < 20; i++)
            {
                PressInsert(grid, pump: false);
            }

            OffscreenGrid.Pump();

            lines.Select(l => l.Name).Should().Equal("Line 1", string.Empty, "Line 2", "Line 3");
            var cells = grid.SelectionController.CurrentCellManager;
            cells.CurrentRowColumnIndex.RowIndex.Should().Be(grid.ResolveToRowIndex(lines[1]));
            cells.CurrentCell.IsEditing.Should().BeTrue();
        });
    }

    [Test]
    public void From_a_table_scrolled_down_the_new_line_is_in_view_whole()
    {
        // From a table scrolled to its bottom, the view mustn't jump back to the top and leave the new line half out of
        // sight.
        OffscreenGrid.Run(() => LinesGrid(lineCount: 30), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var bottom = new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex());
            grid.ScrollInView(bottom);
            OffscreenGrid.Pump();
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[27]), grid.GetFirstColumnIndex()));
            OffscreenGrid.Pump();

            GridRowInserting.InsertAbove(grid);
            OffscreenGrid.Pump();

            lines[27].Name.Should().BeEmpty();
            var row = grid.ResolveToRowIndex(lines[27]);
            var shown = grid.GetVisualContainer().ScrollRows.GetVisibleLineAtLineIndex(row);
            shown.Should().NotBeNull("the new line is in view");
            shown!.ClippedSize.Should().BeApproximately(shown.Size, 0.5, "all of it, not cut off at an edge");
        });
    }

    [Test]
    public void A_half_typed_add_row_is_added_before_the_blank_line_goes_in_under_it()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            // The edit is open in Grams, so the name set on the line isn't overwritten as the edit ends.
            grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex() + 1));
            grid.SelectionController.CurrentCellManager.BeginEdit();
            OffscreenGrid.Pump();
            ((Line)grid.View.CurrentAddItem).Name = "Sugar";

            GridRowInserting.InsertAbove(grid);
            OffscreenGrid.Pump();

            lines.Select(l => l.Name).Should().Equal("Line 1", "Line 2", "Line 3", "Sugar", string.Empty);
            grid.View.IsAddingNew.Should().BeFalse();
        });
    }

    [Test]
    public void Lines_replaced_while_the_new_line_is_open_leave_nothing_open()
    {
        // The screen reloading its lines (another record picked, a Save) while the new line's cell is still open.
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[1]), grid.GetFirstColumnIndex()));

            GridRowInserting.InsertAbove(grid);
            lines.Clear();
            lines.Add(new Line { Name = "Salt", Grams = 20m });
            OffscreenGrid.Pump();

            lines.Select(l => l.Name).Should().Equal("Salt");
            grid.SelectionController.CurrentCellManager.CurrentCell?.IsEditing.Should().NotBe(true);
        });
    }

    [Test]
    public void In_a_sorted_view_the_line_goes_in_above_the_current_one_in_the_lines_own_order()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            grid.SortColumnDescriptions.Add(new SortColumnDescription { ColumnName = nameof(Line.Grams), SortDirection = System.ComponentModel.ListSortDirection.Descending });
            OffscreenGrid.Pump();
            grid.MoveCurrentCell(new RowColumnIndex(grid.ResolveToRowIndex(lines[2]), grid.GetFirstColumnIndex()));

            GridRowInserting.InsertAbove(grid);
            OffscreenGrid.Pump();

            lines.Select(l => l.Name).Should().Equal("Line 1", "Line 2", string.Empty, "Line 3");
            grid.SortColumnDescriptions.Should().BeEmpty();
        });
    }

    /// <summary>A letter typed on the grid as the keyboard types it: preview text input, then text input.</summary>
    private static void TypeLetter(SfDataGrid grid, char letter)
    {
        var target = Keyboard.FocusedElement as UIElement ?? grid;
        var preview = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, target, letter.ToString()))
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
        };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, target, letter.ToString()))
            {
                RoutedEvent = TextCompositionManager.TextInputEvent,
            });
        }
    }

    private static void PressInsert(SfDataGrid grid, bool pump = true)
    {
        var source = PresentationSource.FromVisual(grid)!;
        grid.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, Key.Insert) { RoutedEvent = Keyboard.KeyDownEvent });
        if (pump)
        {
            OffscreenGrid.Pump();
        }
    }

    /// <summary>A 300 px lines table with three lines (or <paramref name="lineCount"/>) and an add row, with the app's line behaviours and inserting on.</summary>
    private static SfDataGrid LinesGrid(int lineCount = 3)
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line>(Enumerable.Range(1, lineCount).Select(i => new Line { Name = $"Line {i}", Grams = i * 100m })),
            AutoGenerateColumns = true,
            AllowEditing = true,
            AllowDeleting = true,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = AddNewRowPosition.Bottom,
            SelectionMode = GridSelectionMode.Single,
            Height = 300,
        };

        GridAddNewRow.SetItemFactory(grid, () => new Line());
        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridEditCommit.SetOnFocusLeave(grid, true);
        GridFillerRows.SetIsEnabled(grid, true);
        GridRowInserting.SetIsEnabled(grid, true);
        return grid;
    }
}
