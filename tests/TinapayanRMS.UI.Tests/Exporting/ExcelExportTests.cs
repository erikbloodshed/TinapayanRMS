using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using AwesomeAssertions;
using Syncfusion.Data;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.XlsIO;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Exporting;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Exporting;

/// <summary>
/// A real grid written to a workbook and read back: what the file holds must be the table as the screen shows it, in its
/// sort and filter, with only its shown columns, its figures as numbers at full precision, and none of GridFillerRows'
/// padding.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class ExcelExportTests
{
    public sealed class Row
    {
        public string Name { get; init; } = string.Empty;

        public decimal Cost { get; init; }

        public int? Pieces { get; init; }

        public string Note { get; init; } = string.Empty;
    }

    [Fact]
    public void A_grid_is_written_sorted_filtered_and_with_its_shown_columns()
    {
        OffscreenGrid.Run(CostsGrid, grid =>
        {
            grid.SortColumnDescriptions.Add(new SortColumnDescription { ColumnName = nameof(Row.Cost), SortDirection = ListSortDirection.Descending });
            grid.Columns[nameof(Row.Name)].FilterPredicates.Add(new FilterPredicate { FilterType = FilterType.NotEquals, FilterValue = "Hopia" });
            grid.Columns[nameof(Row.Note)].IsHidden = true;
            OffscreenGrid.Pump();
            OffscreenGrid.FillerLayer(grid).Layout.RowCount.Should().BePositive("the grid pads the space under its rows, and the padding mustn't reach the file");

            WithSavedWorkbook([new ExcelSheet("Costs", grid)], workbook =>
            {
                var sheet = workbook.Worksheets[0];
                sheet.Name.Should().Be("Costs");
                sheet.UsedRange.LastColumn.Should().Be(3, "the hidden Note column stays out");
                sheet.UsedRange.LastRow.Should().Be(4, "the headings and three products: Hopia is filtered out, and no filler rows");
                CellsOf(sheet, 1).Should().Equal("Name", "Cost", "Pieces");
                CellsOf(sheet, 2).Should().Equal("Mamon", "1,234.50", string.Empty);
                CellsOf(sheet, 3).Should().Equal("Ensaymada", "12.35", "1");
                CellsOf(sheet, 4).Should().Equal("Pandesal", "4.50", "10");
            });
        });
    }

    [Fact]
    public void Figures_are_numbers_at_full_precision_shown_with_the_columns_decimals()
    {
        OffscreenGrid.Run(CostsGrid, grid => WithSavedWorkbook([new ExcelSheet("Costs", grid)], workbook =>
        {
            var sheet = workbook.Worksheets[0];
            var ensaymada = Enumerable.Range(2, 4).Single(row => sheet.Range[row, 1].Text == "Ensaymada");

            sheet.Range[ensaymada, 2].Number.Should().Be(12.34567, "the file keeps the cost the screen rounds to 12.35");
            sheet.Range[ensaymada, 2].NumberFormat.Should().Be("#,##0.00");
            sheet.Range[ensaymada, 3].Number.Should().Be(1);
            sheet.Range[ensaymada, 3].NumberFormat.Should().Be("#,##0");
            sheet.Range[1, 1].CellStyle.Font.Bold.Should().BeTrue("the headings stand out");
        }));
    }

    [Fact]
    public void A_picker_column_is_written_as_the_name_it_shows_not_its_id()
    {
        OffscreenGrid.Run(PickerGrid, grid => WithSavedWorkbook([new ExcelSheet("Changes", grid)], workbook =>
        {
            var sheet = workbook.Worksheets[0];
            CellsOf(sheet, 1).Should().Equal("Raw Material", "New Price");
            CellsOf(sheet, 2).Should().Equal("Bread Flour", "1,375.00");
        }));
    }

    [Fact]
    public void Each_sheet_is_named_as_asked()
    {
        OffscreenGrid.Run(CostsGrid, grid => WithSavedWorkbook(
            [new ExcelSheet("Products", grid), new ExcelSheet("Price Changes", grid)],
            workbook => workbook.Worksheets.Select(sheet => sheet.Name).Should().Equal("Products", "Price Changes")));
    }

    [Fact]
    public void A_file_open_elsewhere_is_left_as_it_was()
    {
        OffscreenGrid.Run(CostsGrid, grid =>
        {
            var path = TemporaryPath();
            File.WriteAllText(path, "open in Excel");
            try
            {
                using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    var save = () => ExcelExport.Save(path, [new ExcelSheet("Costs", grid)]);

                    save.Should().Throw<IOException>();
                }

                File.ReadAllText(path).Should().Be("open in Excel");
            }
            finally
            {
                File.Delete(path);
            }
        });
    }

    [Fact]
    public void The_suggested_name_says_what_and_when()
    {
        ExcelExport.SuggestedFileName("Product Cost Summary", new DateTime(2026, 10, 3)).Should().Be("Product Cost Summary 2026-10-03.xlsx");
    }

    [Theory]
    [InlineData(0, "#,##0")]
    [InlineData(1, "#,##0.0")]
    [InlineData(2, "#,##0.00")]
    [InlineData(4, "#,##0.0000")]
    public void A_figure_keeps_its_columns_decimals_with_a_thousands_separator(int decimals, string format)
    {
        ExcelExport.NumberFormat(decimals).Should().Be(format);
    }

    private static SfDataGrid CostsGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Row>
            {
                new() { Name = "Pandesal", Cost = 4.5m, Pieces = 10, Note = "daily" },
                new() { Name = "Ensaymada", Cost = 12.34567m, Pieces = 1, Note = "weekends" },
                new() { Name = "Hopia", Cost = 99m, Pieces = 6, Note = "boxed" },
                new() { Name = "Mamon", Cost = 1234.5m, Pieces = null, Note = "to order" },
            },
            AutoGenerateColumns = false,
            AllowSorting = true,
            Height = 300,
        };
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Row.Name), HeaderText = "Name" });
        grid.Columns.Add(new NumericColumn { MappingName = nameof(Row.Cost), HeaderText = "Cost", NumberDecimalDigits = 2 });
        grid.Columns.Add(new NumericColumn { MappingName = nameof(Row.Pieces), HeaderText = "Pieces", NumberDecimalDigits = 0, AllowNullValue = true });
        grid.Columns.Add(new GridTextColumn { MappingName = nameof(Row.Note), HeaderText = "Note" });
        GridFillerRows.SetIsEnabled(grid, true);
        return grid;
    }

    public sealed record Material(int Id, string Name);

    public sealed class Change
    {
        public int? MaterialId { get; init; }

        public decimal NewPrice { get; init; }
    }

    /// <summary>A table like What-If's Price Changes: a material picked by id, shown by name.</summary>
    private static SfDataGrid PickerGrid()
    {
        var grid = new SfDataGrid
        {
            ItemsSource = new ObservableCollection<Change> { new() { MaterialId = 7, NewPrice = 1375m } },
            AutoGenerateColumns = false,
            Height = 200,
        };
        grid.Columns.Add(new GridMultiColumnDropDownList
        {
            MappingName = nameof(Change.MaterialId),
            HeaderText = "Raw Material",
            ItemsSource = new[] { new Material(3, "Sugar"), new Material(7, "Bread Flour") },
            DisplayMember = nameof(Material.Name),
            ValueMember = nameof(Material.Id),
        });
        grid.Columns.Add(new NumericColumn { MappingName = nameof(Change.NewPrice), HeaderText = "New Price", NumberDecimalDigits = 2 });
        return grid;
    }

    /// <summary>Saves <paramref name="sheets"/> to a file of their own and hands the workbook read back from it to <paramref name="check"/>.</summary>
    private static void WithSavedWorkbook(IReadOnlyList<ExcelSheet> sheets, Action<IWorkbook> check)
    {
        var path = TemporaryPath();
        try
        {
            ExcelExport.Save(path, sheets);

            using var engine = new ExcelEngine();
            using var file = File.OpenRead(path);
            check(engine.Excel.Workbooks.Open(file));
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>The cells of a row as Excel shows them, through the last column used.</summary>
    private static string[] CellsOf(IWorksheet sheet, int row) =>
        [.. Enumerable.Range(1, sheet.UsedRange.LastColumn).Select(column => sheet.Range[row, column].DisplayText)];

    private static string TemporaryPath() => Path.Combine(Path.GetTempPath(), $"TinapayanRMS export {Guid.NewGuid():N}.xlsx");
}
