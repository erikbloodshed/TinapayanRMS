using System.Globalization;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Converter;
using Syncfusion.XlsIO;

namespace TinapayanRMS.UI.Exporting;

/// <summary>
/// Writes grids to an Excel workbook as the screen shows them, through Syncfusion's grid converter (2026-10-03): the
/// rows in the grid's current sort and filter (its View), the columns the user hasn't hidden, one sheet per grid.
/// What the converter leaves to its caller is done here:
/// <list type="bullet">
/// <item>it writes hidden columns too, so they're left out by name;</item>
/// <item>it formats a figure without a thousands separator ("0.00"), so each figure is written here, with its own
/// column's decimals and one ("#,##0.00");</item>
/// <item>it writes a picker column's id, so its cells go as the name the grid shows;</item>
/// <item>it sizes the columns to the grid's on-screen widths, which Reports' two-line headings make too narrow for
/// one-line ones, so the columns are fitted to what's in them.</item>
/// </list>
/// Figures are written as numbers at full precision, and the formats only show them. A grid's groups go as their caption
/// rows. GridFillerRows' padding is only drawn, never rows of the grid, so there's none to leave out. Each cell is
/// formatted by the grid's own column, as no table has a row's details: a details cell's column would be its
/// <c>GridViewDefinition</c>'s grid's.
/// </summary>
public static class ExcelExport
{
    /// <summary>
    /// Asks where to save a workbook, suggesting "<paramref name="title"/> yyyy-MM-dd.xlsx" in the folder Windows last
    /// saved one in; null when the user cancels.
    /// </summary>
    public static string? AskForPath(Window? owner, string title)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export to Excel",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = SuggestedFileName(title, DateTime.Today),
        };
        var saved = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return saved == true ? dialog.FileName : null;
    }

    /// <summary>"Product Cost Summary 2026-10-03.xlsx": what's in it, and the day it was taken.</summary>
    public static string SuggestedFileName(string title, DateTime day) => $"{title} {day:yyyy-MM-dd}.xlsx";

    /// <summary>
    /// Writes <paramref name="sheets"/> to one workbook at <paramref name="path"/>, replacing a file already there. The
    /// workbook is built in memory first, so a file is only touched once there's a whole workbook to write: a file open
    /// in Excel fails with an IOException and is left as it was.
    /// </summary>
    public static void Save(string path, IReadOnlyList<ExcelSheet> sheets)
    {
        using var engine = new ExcelEngine();
        engine.Excel.DefaultVersion = ExcelVersion.Xlsx;
        var workbook = engine.Excel.Workbooks.Create(sheets.Count);
        for (var i = 0; i < sheets.Count; i++)
        {
            var worksheet = workbook.Worksheets[i];
            worksheet.Name = sheets[i].Name;
            Write(sheets[i].Grid, worksheet);
        }

        using var buffer = new MemoryStream();
        workbook.SaveAs(buffer);
        File.WriteAllBytes(path, buffer.ToArray());
    }

    /// <summary>The Excel number format for a figure shown with <paramref name="decimals"/> places: "#,##0.00" for 2.</summary>
    public static string NumberFormat(int decimals) => decimals > 0 ? $"#,##0.{new string('0', decimals)}" : "#,##0";

    private static void Write(SfDataGrid grid, IWorksheet worksheet)
    {
        var options = new ExcelExportingOptions
        {
            ExcelVersion = ExcelVersion.Xlsx,
            ExportMode = ExportMode.Value,
            CellsExportingEventHandler = (_, e) =>
            {
                var column = grid.Columns[e.ColumnName];
                switch (e.CellType)
                {
                    // A picker column holds an id: its cells go as the name the grid shows.
                    case ExportCellType.RecordCell when column is GridComboBoxColumn or GridMultiColumnDropDownList:
                        e.ExportMode = ExportMode.Text;
                        break;

                    // A figure is written here rather than by the converter, which would format it without a thousands
                    // separator: as a number at full precision, shown with its column's decimals.
                    case ExportCellType.RecordCell when column is GridNumericColumn numeric:
                        if (e.CellValue is { } value)
                        {
                            e.Range.Number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                        }

                        e.Range.NumberFormat = NumberFormat(numeric.NumberDecimalDigits);
                        e.Handled = true;
                        break;
                }
            },
        };
        options.ExcludeColumns.AddRange(grid.Columns.Where(column => column.IsHidden).Select(column => column.MappingName));
        grid.ExportToExcel(grid.View, options, worksheet);

        // The columns as written, left to right from column A, under a heading row; the records from row 2.
        if (options.Columns.Count == 0)
        {
            return;
        }

        worksheet.Range[1, 1, 1, options.Columns.Count].CellStyle.Font.Bold = true;
        if (worksheet.UsedRange.LastRow > 1)
        {
            // The headings stay in view as the rows scroll, as the grid's do.
            worksheet.Range[2, 1].FreezePanes();
        }

        worksheet.UsedRange.AutofitColumns();
    }
}
