using System.IO.Compression;
using AwesomeAssertions;
using Syncfusion.Blazor.Grids;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// A grid's own Excel export as a workbook's bytes (GridExports), as Reports' Export to Excel saves it: Syncfusion hands
/// the workbook back in a stream it has already closed, which read any other way threw "Cannot access a closed Stream".
/// </summary>
public class GridExportsTests
{
    [Test]
    public async Task A_grid_exports_its_rows_as_a_workbook_though_the_stream_comes_back_closed()
    {
        await using var context = AppServices.NewContext();
        var grid = context.Render<SfGrid<Row>>(parameters => parameters
            .Add(g => g.DataSource, [new Row("Pandesal", 4.5m), new Row("Ensaymada", 12m)])
            .Add(g => g.AllowExcelExport, true));

        // What GridExports reads its bytes from, closed by the time it's returned.
        var stream = await grid.Instance.ExportToExcelAsync(asMemoryStream: true, new ExcelExportProperties());
        stream.CanRead.Should().BeFalse("Syncfusion disposes the stream it returns");

        var workbook = await GridExports.WorkbookAsync(grid.Instance, new ExcelExportProperties());

        using var zip = new ZipArchive(new MemoryStream(workbook));
        zip.GetEntry("xl/workbook.xml").Should().NotBeNull("an .xlsx is a zip holding the workbook");
        using var strings = new StreamReader(zip.GetEntry("xl/sharedStrings.xml")!.Open());
        (await strings.ReadToEndAsync()).Should().Contain("Pandesal").And.Contain("Ensaymada");
    }

    public sealed record Row(string Name, decimal Price);
}
