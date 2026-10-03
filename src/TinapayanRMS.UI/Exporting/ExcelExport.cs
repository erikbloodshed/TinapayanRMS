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
/// <item>it formats a figure without a thousands separator ("0.00"), so each numeric column gets the grid's own
/// decimals with one ("#,##0.00");</item>
/// <item>it sizes the columns to the grid's on-screen widths, which Reports' two-line headings make too narrow for
/// one-line ones, so the columns are fitted to what's in them.</item>
/// </list>
/// Figures are written as numbers at full precision, and the formats only show them. GridFillerRows' padding is a
/// grid's unbound rows, which stay out (ExportUnBoundRows is false).
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
        var options = new ExcelExportingOptions { ExcelVersion = ExcelVersion.Xlsx, ExportMode = ExportMode.Value };
        options.ExcludeColumns.AddRange(grid.Columns.Where(column => column.IsHidden).Select(column => column.MappingName));
        grid.ExportToExcel(grid.View, options, worksheet);

        // The columns as written, left to right from column A, under a heading row; the records from row 2.
        var columns = options.Columns.Select(name => grid.Columns[name]).ToList();
        if (columns.Count == 0)
        {
            return;
        }

        worksheet.Range[1, 1, 1, columns.Count].CellStyle.Font.Bold = true;
        var lastRow = worksheet.UsedRange.LastRow;
        if (lastRow > 1)
        {
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i] is GridNumericColumn numeric)
                {
                    worksheet.Range[2, i + 1, lastRow, i + 1].NumberFormat = NumberFormat(numeric.NumberDecimalDigits);
                }
            }

            // The headings stay in view as the rows scroll, as the grid's do.
            worksheet.Range[2, 1].FreezePanes();
        }

        worksheet.UsedRange.AutofitColumns();
    }
}
