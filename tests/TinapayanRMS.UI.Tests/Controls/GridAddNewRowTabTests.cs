using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// A lines table as the app sets them up (the Production Plan's, retired on 2026-10-31 when planning moved to TinapayanPreweigh,
/// and the screens that take lines the same way): a product picked from a drop-down that's typed into, units,
/// and a pieces column Tab skips. Tab from the add row's last cell adds the line and opens the next add row
/// (UntouchedAddRowSelectionController.ProcessKeyDown). A pick reaches the line only when its edit is committed, since
/// AutoCompleteDropDownRenderer makes the column's UpdateTrigger Explicit, so retyping it and pressing Esc leaves the line
/// as it was (2026-10-24, both reported by the user).
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class GridAddNewRowTabTests
{
    public sealed class Product
    {
        public int Id { get; init; }

        public string Name { get; init; } = string.Empty;
    }

    public sealed class Line
    {
        public int? ProductId { get; set; }

        public decimal? Units { get; set; }

        public string Pieces => Units is { } units ? $"{units} pcs" : string.Empty;
    }

    private static readonly List<Product> Products = [new() { Id = 1, Name = "Hopia Ube" }, new() { Id = 2, Name = "Hopia Monggo" }];

    [Fact]
    public void Tab_from_the_last_cell_of_a_typed_add_row_adds_the_line_and_opens_the_next_add_row()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var typed = StartAddRow(grid, column: 1);
            typed.ProductId = 2;
            typed.Units = 20m;

            Press(grid, Key.Tab).Should().BeTrue();

            lines.Should().HaveCount(2).And.Contain(typed);
            var cells = grid.SelectionController.CurrentCellManager;
            cells.CurrentRowColumnIndex.Should().Be(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex()));
            cells.CurrentCell.IsEditing.Should().BeTrue("the next product can be typed straight away");
            grid.View.CurrentAddItem.Should().NotBeSameAs(typed);
        });
    }

    [Fact]
    public void Tab_from_an_earlier_cell_of_the_add_row_moves_along_it()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            var typed = StartAddRow(grid, column: 0);
            typed.ProductId = 2;

            Press(grid, Key.Tab);

            lines.Should().ContainSingle("the line isn't finished yet");
            grid.SelectionController.CurrentCellManager.CurrentRowColumnIndex.Should()
                .Be(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex() + 1));
        });
    }

    [Fact]
    public void Tab_from_an_untouched_add_row_adds_nothing()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var lines = (ObservableCollection<Line>)grid.ItemsSource;
            StartAddRow(grid, column: 1);

            Press(grid, Key.Tab);

            lines.Should().ContainSingle();
        });
    }

    [Fact]
    public void Retyping_a_line_product_writes_nothing_until_the_edit_is_committed_and_Esc_keeps_it()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var line = ((ObservableCollection<Line>)grid.ItemsSource)[0];
            var picker = OpenProductCell(grid);

            // Text matching no product clears the picker's pick as it's typed.
            picker.SelectedItem = null;
            OffscreenGrid.Pump();
            line.ProductId.Should().Be(1, "nothing reaches the line while it's typed");

            Press(grid, Key.Escape);
            line.ProductId.Should().Be(1);

            picker = OpenProductCell(grid);
            picker.SelectedItem = Products[1];
            GridEditCommit.Commit(grid);
            OffscreenGrid.Pump();
            line.ProductId.Should().Be(2, "a committed pick reaches the line");
        });
    }

    [Fact]
    public void Committing_text_that_names_no_product_keeps_the_line_product_but_clearing_the_text_clears_it()
    {
        OffscreenGrid.Run(LinesGrid, grid =>
        {
            var line = ((ObservableCollection<Line>)grid.ItemsSource)[0];

            // Tab, Enter, a click away and Vim's Esc all commit the edit as it stands.
            var picker = OpenProductCell(grid);
            picker.Text = "zzz";
            picker.SelectedItem = null;
            GridEditCommit.Commit(grid);
            OffscreenGrid.Pump();
            line.ProductId.Should().Be(1, "text naming no product isn't a pick");

            picker = OpenProductCell(grid);
            picker.Text = string.Empty;
            picker.SelectedItem = null;
            GridEditCommit.Commit(grid);
            OffscreenGrid.Pump();
            line.ProductId.Should().BeNull("emptying the box takes the product out, as AllowNullInput means it to");
        });
    }

    [Fact]
    public void A_plan_add_row_left_untouched_is_dropped_not_added_as_a_blank_line()
    {
        // A line type whose add row Tab opens straight after a line (the Production Plan's was the first): an add row is only
        // known to be untouched against a row made with a parameterless constructor.
        var lines = new ObservableCollection<Line>();
        OffscreenGrid.Run(
            () =>
            {
                var grid = new SfDataGrid
                {
                    ItemsSource = lines,
                    AutoGenerateColumns = false,
                    AllowEditing = true,
                    EditTrigger = EditTrigger.OnTap,
                    AddNewRowPosition = AddNewRowPosition.Bottom,
                    SelectionMode = GridSelectionMode.Single,
                    Height = 300,
                };
                grid.Columns.Add(new GridMultiColumnDropDownList { MappingName = "ProductId", ItemsSource = Products, DisplayMember = nameof(Product.Name), ValueMember = nameof(Product.Id), AllowNullInput = true });
                grid.Columns.Add(new NumericColumn { MappingName = "Units", AllowNullValue = true });
                grid.Columns.Add(new GridTextColumn { MappingName = nameof(Line.Pieces), AllowEditing = false, AllowFocus = false });
                AutoCompleteDropDown.SetIsEnabled(grid, true);
                GridAddNewRow.SetDropWhenUntouched(grid, true);
                GridAddNewRow.SetItemFactory(grid, () => new Line());
                return grid;
            },
            grid =>
            {
                grid.Focus();
                grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex()));
                grid.SelectionController.CurrentCellManager.BeginEdit();
                OffscreenGrid.Pump();

                GridEditCommit.Commit(grid);
                OffscreenGrid.Pump();

                lines.Should().BeEmpty();
                grid.View.IsAddingNew.Should().BeFalse();
            });
    }

    /// <summary>Opens the add row in <paramref name="column"/> (0 for Product) and returns its line.</summary>
    private static Line StartAddRow(SfDataGrid grid, int column)
    {
        grid.Focus();
        grid.MoveCurrentCell(new RowColumnIndex(grid.GetAddNewRowIndex(), grid.GetFirstColumnIndex() + column));
        grid.SelectionController.CurrentCellManager.BeginEdit();
        OffscreenGrid.Pump();
        return (Line)grid.View.CurrentAddItem;
    }

    /// <summary>Opens the first line's Product cell and returns its picker.</summary>
    private static SfMultiColumnDropDownControl OpenProductCell(SfDataGrid grid)
    {
        grid.Focus();
        grid.MoveCurrentCell(new RowColumnIndex(grid.GetFirstDataRowIndex(), grid.GetFirstColumnIndex()));
        grid.SelectionController.CurrentCellManager.BeginEdit();
        OffscreenGrid.Pump();
        return Descendants(grid).OfType<SfMultiColumnDropDownControl>().First(p => p.IsVisible);
    }

    /// <summary>Presses <paramref name="key"/> on the grid, as the grid takes a key from its current cell; true when it was handled.</summary>
    private static bool Press(SfDataGrid grid, Key key)
    {
        var args = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(grid)!, 0, key) { RoutedEvent = Keyboard.KeyDownEvent };
        grid.SelectionController.HandleKeyDown(args);
        OffscreenGrid.Pump();
        return args.Handled;
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

    /// <summary>One line and an add row, with the lines table's columns and add-row behaviours.</summary>
    private static SfDataGrid LinesGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Line> { new() { ProductId = 1, Units = 50m } },
            AutoGenerateColumns = false,
            AllowEditing = true,
            AllowDeleting = true,
            EditTrigger = EditTrigger.OnTap,
            AddNewRowPosition = AddNewRowPosition.Bottom,
            SelectionMode = GridSelectionMode.Single,
            ShowRowHeader = true,
            Height = 300,
        };
        grid.Columns.Add(new GridMultiColumnDropDownList
        {
            MappingName = nameof(Line.ProductId),
            ItemsSource = Products,
            DisplayMember = nameof(Product.Name),
            ValueMember = nameof(Product.Id),
            AllowAutoComplete = true,
            AllowNullInput = true,
        });
        grid.Columns.Add(new NumericColumn { MappingName = nameof(Line.Units), AllowNullValue = true });
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Line.Pieces), AllowEditing = false, AllowFocus = false });
        AutoCompleteDropDown.SetIsEnabled(grid, true);
        GridAddNewRow.SetDropWhenUntouched(grid, true);
        GridAddNewRow.SetItemFactory(grid, () => new Line());
        GridEditCommit.SetOnFocusLeave(grid, true);
        return grid;
    }
}
