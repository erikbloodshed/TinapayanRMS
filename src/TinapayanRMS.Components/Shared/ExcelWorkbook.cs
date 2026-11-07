using Syncfusion.XlsIO;

namespace TinapayanRMS.Components.Shared;

/// <summary>One column of an exported sheet: its heading, each row's value, and how a number shows ("#,##0.00").</summary>
public sealed record ExcelColumn<TRow>(string Header, Func<TRow, object?> Value, string? Format = null);

/// <summary>One sheet of an exported workbook: its name, and its heading row over its rows' values, column by column.</summary>
public sealed record ExcelSheet(string Name, IReadOnlyList<string> Headers, IReadOnlyList<string?> Formats, IReadOnlyList<object?[]> Rows)
{
    public static ExcelSheet Of<TRow>(string name, IEnumerable<TRow> rows, params ExcelColumn<TRow>[] columns) => new(
        name,
        [.. columns.Select(c => c.Header)],
        [.. columns.Select(c => c.Format)],
        [.. rows.Select(row => columns.Select(c => c.Value(row)).ToArray())]);
}

/// <summary>
/// A workbook of several sheets, as the WPF app's ExcelExport wrote from its grids (Syncfusion XlsIO, which the licence
/// key covers): the Price Simulator's products and price changes.
/// Each sheet has a bold heading row, numbers as numbers in the screen's decimals, and columns as wide as their text.
/// </summary>
public static class ExcelWorkbook
{
    /// <summary>What the Save dialog calls a workbook.</summary>
    public const string Kind = "Excel workbook";

    public const string Money = "#,##0.00";
    public const string Rate = "#,##0.0000";
    public const string Whole = "#,##0";
    public const string OneDecimal = "#,##0.0";

    public static byte[] Build(params ExcelSheet[] sheets)
    {
        using var engine = new ExcelEngine();
        var application = engine.Excel;
        application.DefaultVersion = ExcelVersion.Xlsx;
        var workbook = application.Workbooks.Create(sheets.Length);

        for (var s = 0; s < sheets.Length; s++)
        {
            var sheet = sheets[s];
            var worksheet = workbook.Worksheets[s];
            worksheet.Name = sheet.Name;

            for (var c = 0; c < sheet.Headers.Count; c++)
            {
                var heading = worksheet.Range[1, c + 1];
                heading.Text = sheet.Headers[c];
                heading.CellStyle.Font.Bold = true;
            }

            for (var r = 0; r < sheet.Rows.Count; r++)
            {
                for (var c = 0; c < sheet.Headers.Count; c++)
                {
                    var cell = worksheet.Range[r + 2, c + 1];
                    switch (sheet.Rows[r][c])
                    {
                        case null:
                            break;
                        case decimal or double or float or int or long:
                            cell.Number = Convert.ToDouble(sheet.Rows[r][c], System.Globalization.CultureInfo.InvariantCulture);
                            if (sheet.Formats[c] is { } format)
                            {
                                cell.NumberFormat = format;
                            }

                            break;
                        case DateTime date:
                            cell.DateTime = date;
                            cell.NumberFormat = sheet.Formats[c] ?? "mmm d, yyyy";
                            break;
                        case var text:
                            cell.Text = text.ToString();
                            break;
                    }
                }
            }

            if (sheet.Headers.Count > 0)
            {
                worksheet.UsedRange.AutofitColumns();
                worksheet.Range[1, 1, 1, sheet.Headers.Count].FreezePanes();
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
