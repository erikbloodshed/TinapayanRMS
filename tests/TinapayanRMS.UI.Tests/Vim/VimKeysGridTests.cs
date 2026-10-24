using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>
/// The Vim keys in a real lines table, set up as the app's are (GridAddNewRow, GridEditCommit, GridFillerRows, OnTap
/// editing). The keys are raised on the grid as the keyboard raises them on the element that has it: a letter as text
/// input, Esc as a key press. On, they move and act; off, the grid does what it always did.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class VimKeysGridTests
{
    public sealed class Line
    {
        public string Name { get; set; } = string.Empty;

        public decimal Grams { get; set; }
    }

    [Fact]
    public void J_and_k_move_the_current_cell_down_and_up_a_line()
    {
        RunWithVimKeys(() => LinesGrid(lineCount: 3), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);
            var first = grid.GetFirstDataRowIndex();

            Type(grid, "j");
            CurrentRow(grid).Should().Be(first + 1);

            Type(grid, "jk");
            CurrentRow(grid).Should().Be(first + 1);
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeFalse("moving starts no edit");
        });
    }

    [Fact]
    public void G_goes_to_the_last_line_not_a_filler_row_and_gg_back_to_the_first()
    {
        RunWithVimKeys(() => LinesGrid(lineCount: 3), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            GridKeyboardFocus.PlaceCurrentCell(grid);
            var column = grid.GetFirstColumnIndex() + 1;
            grid.MoveCurrentCell(new(grid.GetFirstDataRowIndex(), column));

            Type(grid, "G");
            CurrentRow(grid).Should().Be(grid.ResolveToRowIndex(lines[^1]));
            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.ColumnIndex.Should().Be(column, "G keeps the column, as Vim keeps it");

            Type(grid, "gg");
            CurrentRow(grid).Should().Be(grid.ResolveToRowIndex(lines[0]));
        });
    }

    [Fact]
    public void Zero_and_dollar_go_to_the_first_and_last_column()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "$");
            CurrentColumn(grid).Should().Be(grid.GetLastColumnIndex());

            Type(grid, "0");
            CurrentColumn(grid).Should().Be(grid.GetFirstColumnIndex());
        });
    }

    [Fact]
    public void A_letter_that_isnt_a_key_of_ours_starts_no_edit()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "x");

            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeFalse();
            ((ObservableCollection<Line>)grid.ItemsSource)[0].Name.Should().Be("Line 1");
        });
    }

    [Fact]
    public void With_the_keys_off_a_letter_starts_an_edit_as_it_always_did()
    {
        OffscreenGrid.Run(() => LinesGrid(), grid =>
        {
            VimKeys.Register();
            VimKeys.State.IsEnabled.Should().BeFalse();
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "x");

            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeTrue();
        });
    }

    [Fact]
    public void I_starts_an_edit_and_Esc_ends_it_keeping_what_was_typed()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "i");
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeTrue();

            var editor = Editor(grid);
            TypeOne(editor, 'q').Should().BeFalse("while a cell is being typed into, every key types");
            editor.Text = "Rye flour";

            Press(editor, Key.Escape).Should().BeTrue();

            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeFalse();
            ((ObservableCollection<Line>)grid.ItemsSource)[0].Name.Should().Be("Rye flour", "Esc keeps the value, where the grid on its own throws it away");
        });
    }

    [Fact]
    public void O_types_a_new_line_on_the_add_row_and_Esc_adds_it()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "o");
            CurrentRow(grid).Should().Be(grid.GetAddNewRowIndex());
            grid.View.IsAddingNew.Should().BeTrue();

            Editor(grid).Text = "Salt";
            Press(Editor(grid), Key.Escape);

            lines.Should().HaveCount(3);
            lines[^1].Name.Should().Be("Salt");
        });
    }

    [Fact]
    public void Capital_o_types_a_new_line_put_in_above_the_current_one()
    {
        RunWithVimKeys(
            () =>
            {
                var grid = LinesGrid();
                GridAddNewRow.SetItemFactory(grid, () => new Line());
                GridRowInserting.SetIsEnabled(grid, true);
                return grid;
            },
            grid =>
            {
                var lines = (ObservableCollection<Line>)grid.ItemsSource;
                GridKeyboardFocus.PlaceCurrentCell(grid);
                Type(grid, "j");

                Type(grid, "O");
                OffscreenGrid.Pump();

                lines.Should().HaveCount(3);
                lines[1].Name.Should().BeEmpty();
                CurrentRow(grid).Should().Be(grid.ResolveToRowIndex(lines[1]));
                Editor(grid).Text = "Salt";
                Press(Editor(grid), Key.Escape);

                lines[1].Name.Should().Be("Salt");
                lines[2].Name.Should().Be("Line 2");
            });
    }

    [Fact]
    public void Dd_deletes_the_line()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "d");
            lines.Should().HaveCount(2, "d on its own waits for its second key");
            VimKeys.State.Pending.Should().Be('d');

            Type(grid, "d");
            lines.Should().ContainSingle().Which.Name.Should().Be("Line 2");
            VimKeys.State.Pending.Should().Be('\0');
        });
    }

    [Fact]
    public void Esc_drops_a_half_typed_sequence()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);
            var lines = (ObservableCollection<Line>)grid.ItemsSource;

            Type(grid, "d");
            Press(grid, Key.Escape).Should().BeTrue("the Esc was the Vim keys'");
            Type(grid, "d");

            lines.Should().HaveCount(2, "the second d started a new dd");
            VimKeys.State.Pending.Should().Be('d');
        });
    }

    [Fact]
    public void The_mode_shows_Insert_while_a_cell_is_typed_into_and_Normal_after()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "i");
            VimKeys.State.Mode.Should().Be(VimMode.Insert);

            Press(Editor(grid), Key.Escape);
            VimKeys.State.Mode.Should().Be(VimMode.Normal);
        });
    }

    [Fact]
    public void Insert_lasts_from_cell_to_cell_so_a_digit_on_the_next_cell_starts_typing_until_Esc()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);
            var lines = (ObservableCollection<Line>)grid.ItemsSource;

            Type(grid, "i");
            Editor(grid).Text = "Rye flour";
            Press(Editor(grid), Key.Tab);

            var cell = grid.SelectionController.CurrentCellManager.CurrentCell;
            cell.IsEditing.Should().BeFalse("Tab ends the cell's edit and moves on");
            CurrentColumn(grid).Should().Be(grid.GetFirstColumnIndex() + 1);
            VimKeys.State.Mode.Should().Be(VimMode.Insert, "the keys stay in Insert until Esc");

            TypeOne(grid, '2').Should().BeFalse("in Insert every key types, a digit included");
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeTrue("the digit starts the cell's edit, as it does with the keys off");

            Press(Editor(grid), Key.Escape).Should().BeTrue();
            VimKeys.State.Mode.Should().Be(VimMode.Normal);
            lines[0].Name.Should().Be("Rye flour");

            Type(grid, "j");
            CurrentRow(grid).Should().Be(grid.GetFirstDataRowIndex() + 1, "back in Normal, j moves");
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeFalse();
        });
    }

    [Fact]
    public void Esc_on_a_cell_not_being_typed_into_takes_Insert_back_to_Normal()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);

            Type(grid, "i");
            Press(Editor(grid), Key.Tab);
            VimKeys.State.Mode.Should().Be(VimMode.Insert);

            Press(grid, Key.Escape).Should().BeTrue("the Esc was the Vim keys'");
            VimKeys.State.Mode.Should().Be(VimMode.Normal);

            TypeOne(grid, 'x').Should().BeTrue("in Normal no letter types");
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeFalse();
        });
    }

    /// <summary>
    /// A column filter's search box types, though its pop-up hangs from the grid's heading: a letter there isn't the grid's
    /// Normal-mode key. The filter control is put in the heading as Syncfusion's own funnel click does (GridHeaderCellControl's
    /// OpenFilterPopUp), with its pop-up left shut, since one opened from an off-screen grid is moved onto a real screen.
    /// </summary>
    [Fact]
    public void A_letter_types_in_a_column_filters_search_box()
    {
        RunWithVimKeys(FilteredGrid, grid =>
        {
            var heading = Descendants(grid).OfType<GridHeaderCellControl>().First(cell => cell.Column?.MappingName == nameof(Line.Name));
            heading.ApplyTemplate();
            var filterControl = new GridFilterControl();
            ((Border)heading.Template.FindName("PART_FilterPopUpPresenter", heading)).Child = filterControl;
            filterControl.ApplyTemplate();
            var list = (CheckboxFilterControl)filterControl.Template.FindName("PART_CheckboxFilterControl", filterControl);
            list.ApplyTemplate();
            var searchBox = (TextBox)list.Template.FindName("PART_SearchTextBox", list);

            TypeOne(searchBox, 'f').Should().BeFalse("the search box is typed into, not the grid's Normal mode");
            TypeOne(grid, 'x').Should().BeTrue("on the grid itself, a letter is still a Normal-mode key");
        });
    }

    [Fact]
    public void Ctrl_r_then_equals_types_the_answer_to_a_sum_into_the_cell()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var editor = EditGrams(grid);

            PressWithControl(editor, Key.R).Should().BeTrue("Ctrl+R in Insert waits for a register");
            VimKeys.State.Indicator.Should().Be("INSERT \"");
            TypeOne(editor, '=').Should().BeTrue();
            VimKeys.State.Mode.Should().Be(VimMode.Command);

            Type(editor, "5*");
            Press(editor, Key.Space).Should().BeTrue("a space goes in the sum, not the number box");
            Type(editor, "(2+3)");
            Bar(grid).Should().Be("=5* (2+3)▏");
            editor.IsKeyboardFocusWithin.Should().BeTrue("the keyboard stays in the cell while the sum is typed");

            Press(editor, Key.Enter).Should().BeTrue();
            VimKeys.State.Mode.Should().Be(VimMode.Insert);
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeTrue("the answer is typed, not committed");

            Press(editor, Key.Escape);
            lines[0].Grams.Should().Be(25m);
        });
    }

    [Fact]
    public void The_answer_is_rounded_to_the_decimals_the_cell_takes()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            var editor = EditGrams(grid);

            TypeSum(editor, "20/3");
            Press(editor, Key.Escape);

            ((ObservableCollection<Line>)grid.ItemsSource)[0].Grams.Should().Be(6.67m, "the column shows and takes 2 decimals");
        });
    }

    [Fact]
    public void A_sum_that_cant_be_worked_out_keeps_the_line_up_saying_why()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            var editor = EditGrams(grid);

            TypeSum(editor, "5*(2+3");

            VimKeys.State.Mode.Should().Be(VimMode.Command, "the line stays up to be put right");
            Bar(grid).Should().Contain("a ) is missing");

            Type(editor, ")");
            Press(editor, Key.Enter);
            Press(editor, Key.Escape);
            ((ObservableCollection<Line>)grid.ItemsSource)[0].Grams.Should().Be(25m);
        });
    }

    [Fact]
    public void Esc_puts_the_sum_away_leaving_the_cell_as_it_was()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            var editor = EditGrams(grid);
            PressWithControl(editor, Key.R);
            Type(editor, "=7*7");

            Press(editor, Key.Escape).Should().BeTrue();

            VimKeys.State.Mode.Should().Be(VimMode.Insert);
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeTrue("Esc on the line doesn't end the cell's edit");
            Press(editor, Key.Escape);
            ((ObservableCollection<Line>)grid.ItemsSource)[0].Grams.Should().Be(100m);
        });
    }

    [Fact]
    public void Between_cells_in_Insert_the_answer_begins_the_cells_edit()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            GridKeyboardFocus.PlaceCurrentCell(grid);
            Type(grid, "i");
            Press(Editor(grid), Key.Tab);
            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeFalse();

            TypeSum(grid, "4*30");

            grid.SelectionController.CurrentCellManager.CurrentCell.IsEditing.Should().BeTrue();
            Press(Editor(grid), Key.Escape);
            ((ObservableCollection<Line>)grid.ItemsSource)[0].Grams.Should().Be(120m);
            ((ObservableCollection<Line>)grid.ItemsSource)[0].Name.Should().Be("Line 1");
        });
    }

    [Fact]
    public void Another_character_after_ctrl_r_is_dropped()
    {
        RunWithVimKeys(() => LinesGrid(), grid =>
        {
            var editor = EditGrams(grid);

            PressWithControl(editor, Key.R);
            TypeOne(editor, 'a').Should().BeTrue("there's no register a");

            VimKeys.State.Indicator.Should().Be("INSERT");
            TypeOne(editor, '7').Should().BeFalse("the next key types again");
        });
    }

    /// <summary>Begins the first line's Grams edit with <c>i</c>, everything in it selected as a click leaves it.</summary>
    private static TextBox EditGrams(SfDataGrid grid)
    {
        GridKeyboardFocus.PlaceCurrentCell(grid);
        grid.MoveCurrentCell(new(grid.GetFirstDataRowIndex(), grid.GetFirstColumnIndex() + 1));
        Type(grid, "i");
        var editor = Editor(grid);
        editor.SelectAll();
        return editor;
    }

    /// <summary>Ctrl+R, =, <paramref name="sum"/>, then Enter.</summary>
    private static void TypeSum(UIElement target, string sum)
    {
        PressWithControl(target, Key.R);
        Type(target, "=" + sum);
        Press(target, Key.Enter);
    }

    /// <summary>What the line along the bottom of <paramref name="grid"/>'s window says while it takes a sum.</summary>
    private static string Bar(SfDataGrid grid)
    {
        var layer = AdornerLayer.GetAdornerLayer(grid)!;
        return Descendants(layer).OfType<TextBlock>()
            .Where(text => text.IsVisible)
            .Select(text => text.Text)
            .Aggregate(string.Empty, (shown, text) => shown + text);
    }

    /// <summary>Presses <paramref name="key"/> with Ctrl held, as the keyboard reports it to the thread.</summary>
    private static bool PressWithControl(UIElement target, Key key)
    {
        var held = new byte[256];
        GetKeyboardState(held).Should().BeTrue();
        var pressed = (byte[])held.Clone();
        pressed[VkControl] = pressed[VkLeftControl] = 0x80;
        SetKeyboardState(pressed);
        try
        {
            return Press(target, key);
        }
        finally
        {
            SetKeyboardState(held);
        }
    }

    private const int VkControl = 0x11;
    private const int VkLeftControl = 0xA2;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GetKeyboardState(byte[] keyState);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool SetKeyboardState(byte[] keyState);

    /// <summary>Runs <paramref name="test"/> on <paramref name="grid"/> with the Vim keys on, and off again after.</summary>
    private static void RunWithVimKeys(Func<SfDataGrid> create, Action<SfDataGrid> test) =>
        OffscreenGrid.Run(create, shown =>
        {
            VimKeys.Register();
            VimKeys.State.IsEnabled = true;
            try
            {
                test(shown);
            }
            finally
            {
                VimKeys.State.IsEnabled = false;
            }
        });

    /// <summary>Types <paramref name="text"/> on <paramref name="target"/> as the keyboard does.</summary>
    private static void Type(UIElement target, string text)
    {
        foreach (var character in text)
        {
            TypeOne(target, character);
        }
    }

    /// <summary>Types <paramref name="character"/> on <paramref name="target"/> as the keyboard does: preview text input,
    /// then text input unless the preview was taken. True when it was.</summary>
    private static bool TypeOne(UIElement target, char character)
    {
        var preview = new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, target, character.ToString()))
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent,
        };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, new TextComposition(InputManager.Current, target, character.ToString()))
            {
                RoutedEvent = TextCompositionManager.TextInputEvent,
            });
        }

        Settle();
        return preview.Handled;
    }

    /// <summary>Presses <paramref name="key"/> on <paramref name="target"/>; true when its preview was taken.</summary>
    private static bool Press(UIElement target, Key key)
    {
        var source = PresentationSource.FromVisual(target)!;
        var preview = new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        target.RaiseEvent(preview);
        if (!preview.Handled)
        {
            target.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = Keyboard.KeyDownEvent });
        }

        Settle();
        return preview.Handled;
    }

    /// <summary>Lets what the keys queued (an editor loading, the caret placed) run.</summary>
    private static void Settle()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
        Dispatcher.PushFrame(frame);
    }

    /// <summary>The text box of the cell being typed into.</summary>
    private static TextBox Editor(SfDataGrid grid) =>
        Descendants(grid).OfType<TextBox>().Single(box => box.IsVisible);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static int CurrentRow(SfDataGrid grid) => grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.RowIndex;

    private static int CurrentColumn(SfDataGrid grid) => grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.ColumnIndex;

    /// <summary>
    /// A 300 px lines table, editable, with an add row and filler rows, its columns declared as the app's are (a
    /// NumericColumn for the grams, which takes 2 decimals).
    /// </summary>
    private static SfDataGrid LinesGrid(int lineCount = 2)
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line>(Enumerable.Range(1, lineCount).Select(i => new Line { Name = $"Line {i}", Grams = i * 100m })),
            AutoGenerateColumns = false,
            AllowEditing = true,
            AllowDeleting = true,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = AddNewRowPosition.Bottom,
            SelectionMode = GridSelectionMode.Single,
            Height = 300,
        };
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Line.Name) });
        grid.Columns.Add(new NumericColumn { MappingName = nameof(Line.Grams), NumberDecimalDigits = 2 });

        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridEditCommit.SetOnFocusLeave(grid, true);
        GridFillerRows.SetIsEnabled(grid, true);
        return grid;
    }

    /// <summary>A read-only table whose Name column filters from a funnel in its heading, as Reports' and the Price Simulator's do.</summary>
    private static SfDataGrid FilteredGrid() => new()
    {
        ItemsSource = new ObservableCollection<Line>([new Line { Name = "Flour" }, new Line { Name = "Sugar" }]),
        AutoGenerateColumns = false,
        AllowEditing = false,
        SelectionMode = GridSelectionMode.Extended,
        NavigationMode = NavigationMode.Row,
        Height = 300,
        Columns = { new GridTextColumn { MappingName = nameof(Line.Name), AllowFiltering = true } },
    };
}
