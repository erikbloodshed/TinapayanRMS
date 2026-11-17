using Syncfusion.Blazor.Grids;

namespace TinapayanRMS.Components.Shared;

/// <summary>A grid's own Excel export, as the bytes of a workbook rather than the browser's download it would otherwise start.</summary>
public static class GridExports
{
    /// <summary>
    /// The workbook <paramref name="grid"/> exports as it's shown: its sort, filters and columns. Syncfusion closes the
    /// stream it hands back (it disposes it as it returns it), so it's read with ToArray, which a closed MemoryStream still
    /// allows: setting its Position or copying it threw "Cannot access a closed Stream", and Reports' Export to Excel failed.
    /// </summary>
    public static async Task<byte[]> WorkbookAsync<TRow>(SfGrid<TRow> grid, ExcelExportProperties properties) =>
        (await grid.ExportToExcelAsync(asMemoryStream: true, properties)).ToArray();
}
