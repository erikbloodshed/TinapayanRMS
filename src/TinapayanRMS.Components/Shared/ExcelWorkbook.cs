using Syncfusion.XlsIO;

namespace TinapayanRMS.Components.Shared;

/// <summary>One column of an exported sheet: its heading, each row's value, and how a number shows ("#,##0.00").</summary>
public sealed record ExcelColumn<TRow>(string Header, Func<TRow, object?> Value, string? Format = null);

/// <summary>
/// A value with a look of its own in place of its column's: a number format of its own (the Costing sheet's markup,
/// "(30.0%) 30.00", still a number), or bold (a heading row partway down a sheet).
/// </summary>
public sealed record ExcelCell(object? Value, string? Format = null, bool Bold = false);

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
/// A workbook of several sheets, written with Syncfusion XlsIO (which the licence key covers): the Price Simulator's
/// products and price changes.
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
                    Write(worksheet.Range[r + 2, c + 1], sheet.Rows[r][c], sheet.Formats[c]);
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

    private static void Write(IRange cell, object? value, string? format)
    {
        switch (value)
        {
            case null:
                break;
            case ExcelCell own:
                cell.CellStyle.Font.Bold = own.Bold;
                Write(cell, own.Value, own.Format ?? format);
                break;
            case decimal or double or float or int or long:
                cell.Number = Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture);
                if (format is not null)
                {
                    cell.NumberFormat = format;
                }

                break;
            case DateTime date:
                cell.DateTime = date;
                cell.NumberFormat = format ?? "mmm d, yyyy";
                break;
            default:
                cell.Text = value.ToString();
                break;
        }
    }
}
