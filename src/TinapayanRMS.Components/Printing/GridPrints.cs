using System.Globalization;
using Syncfusion.Blazor.Grids;
using TinapayanRMS.Presentation.Printing;

namespace TinapayanRMS.Components.Printing;

/// <summary>
/// A Reports table as a printout, as it's shown, as its Export to Excel saves it: the columns showing, in their order, and
/// the rows its filters leave, in its sort (the grid has no paging, so its current view is all of them), each cell in its
/// column's format.
/// </summary>
public static class GridPrints
{
    public static async Task<TablePrintout> PrintoutAsync<TRow>(SfGrid<TRow> grid, string title, string caption, DateTime printedAt)
    {
        var columns = (await grid.GetColumnsAsync()).Where(c => c.Visible && !string.IsNullOrEmpty(c.Field)).ToList();
        var rows = (await grid.GetCurrentViewRecordsAsync()).ToList();
        var properties = columns.Select(c => typeof(TRow).GetProperty(c.Field)).ToList();

        var table = new PrintedTable(
            null,
            [.. columns.Select(c => new PrintedColumn(c.HeaderText ?? c.Field, c.TextAlign == TextAlign.Right, c.Type == ColumnType.Date))],
            [.. rows.Select(row => (IReadOnlyList<string>)[.. columns.Select((c, i) => Cell(properties[i]?.GetValue(row), c.Format))])]);
        return new TablePrintout(title, [caption, rows.Count == 1 ? "1 row" : $"{rows.Count:N0} rows"], [table], printedAt);
    }

    /// <summary>A value as its column shows it: in the column's format ("N2", "MMM d, yyyy"), a tick for a true, nothing for null.</summary>
    public static string Cell(object? value, string? format) => value switch
    {
        null => "",
        bool flag => flag ? "Yes" : "",
        IFormattable formattable when !string.IsNullOrEmpty(format) => formattable.ToString(format, CultureInfo.CurrentCulture),
        _ => Convert.ToString(value, CultureInfo.CurrentCulture) ?? "",
    };
}
