using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Syncfusion.XlsIO;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>
/// Printouts of tables (a Reports table, a sub-assembly) and a product's costing to Excel: the table printed under its
/// title with its figures at the right and its total once; a grid's cells in their columns' formats; the preview's bar
/// for tables; and the costing workbook's sheets.
/// </summary>
public class TablePrintoutTests
{
    [Test]
    public async Task A_table_printout_is_one_part_under_its_title_its_figures_at_the_right_and_its_total_once()
    {
        await using var context = AppServices.NewContext();

        var document = context.Render<PrintDocument>(parameters => parameters.Add(d => d.Tables, Printout()));

        document.FindAll(".print-sheet").Should().ContainSingle();
        document.Find(".print-title h1").TextContent.Should().Be("Raw Materials Price Summary");
        document.FindAll(".print-title .print-muted").Select(p => p.TextContent).Should().Equal("Every purchased material's latest price.", "2 rows");
        document.FindAll("thead th").Select(th => (th.TextContent, th.ClassName ?? "")).Should().Equal(("Raw Material", ""), ("Price", "n"), ("Since", "nw"));
        document.FindAll("tbody tr").Should().HaveCount(2);
        document.Find("tfoot tr").TextContent.Should().Contain("Total");
        document.Find(".print-block .print-small").TextContent.Should().Be("Prices with VAT.");
        document.FindAll(".print-draft").Should().BeEmpty();
    }

    [Test]
    public async Task A_table_printout_with_changes_not_saved_is_a_draft()
    {
        await using var context = AppServices.NewContext();

        var document = context.Render<PrintDocument>(parameters => parameters.Add(d => d.Tables, Printout() with { SaveNote = ProductPrintout.UnsavedChangesNote }));

        document.Find(".print-draft").TextContent.Should().StartWith("Draft: it includes changes not yet saved.");
        document.FindAll(".print-draft-mark").Should().ContainSingle();
    }

    [Test]
    public async Task The_preview_of_tables_has_no_Include_ticks_and_saves_its_PDF_under_the_title_and_date()
    {
        await using var context = AppServices.NewContext();
        var dialogs = new FakeFileDialogs();
        context.Services.AddSingleton<IFileDialogs>(dialogs);

        var pane = context.Render<PrintPane>(parameters => parameters.Add(p => p.Tables, Printout()));
        await pane.InvokeAsync(() => pane.FindAll("button").Single(b => b.TextContent.Trim() == "Export to PDF").Click());

        pane.FindAll(".print-bar .e-checkbox-wrapper").Should().BeEmpty();
        dialogs.SavesAsked.Should().Equal(("Raw Materials Price Summary 2026-11-19.pdf", "PDF document"));
    }

    [Test]
    public void A_grid_cell_prints_in_its_columns_format()
    {
        GridPrints.Cell(1234.5m, "N2").Should().Be("1,234.50");
        GridPrints.Cell(new DateTime(2026, 11, 19), "MMM d, yyyy").Should().Be("Nov 19, 2026");
        GridPrints.Cell(null, "N2").Should().Be("");
        GridPrints.Cell(true, null).Should().Be("Yes");
        GridPrints.Cell("Flour", null).Should().Be("Flour");
    }

    [Test]
    public void A_products_costing_workbook_has_its_costing_recipe_and_packaging_sheets()
    {
        var printout = new ProductPrintout(
            "Ube/Roll", "Breads", "Production", "Per piece", "—", null, new DateTime(2026, 11, 19),
            [new PrintedSection("Dough", null, "Baker's %", [new PrintedLine("Flour", 1000m, 100m, 0.052m, 52m)], [],
                1000m, 100m, 52m, 50m, 20m, 2.6m, true)],
            new PrintedPackaging(null, [new PrintedPackagingLine("Glassine", 1m, "pc", 0.43m, 0.43m)], 0.43m),
            new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 2.6m)], MarkupPercent = 30m, VatPercent = 12m, CurrentPrice = 10m }));

        var bytes = CostingWorkbook.Build(printout);

        CostingWorkbook.FileNameOf(printout, new DateTime(2026, 11, 19)).Should().Be("Ube-Roll Costing 2026-11-19.xlsx");
        using var engine = new ExcelEngine();
        using var stream = new MemoryStream(bytes);
        var workbook = engine.Excel.Workbooks.Open(stream);
        // Without the licence key, which the tests don't have, XlsIO adds a sheet of its own after them.
        workbook.Worksheets.Select(s => s.Name).Where(name => name != "Evaluation Warning").Should().Equal("Costing", "Recipe", "Packaging");
        var costing = workbook.Worksheets["Costing"];
        costing.Range["A1"].Text.Should().Be("Component");
        costing.Range["B1"].Text.Should().Be("Per piece");
        costing.Range["A2"].Text.Should().Be("Sub-assemblies (ingredients)");
        costing.Range["B2"].Number.Should().Be(2.6);

        // Under the subtotal, after a blank row, the suggested price beside the current one in the breakdown's columns,
        // as the Costing tab has them, each markup with its percentage before it in the cell's format.
        var costs = printout.Costs;
        var heading = Enumerable.Range(1, costing.UsedRange.LastRow).Single(row => costing.Range[row, 2].Text == "Suggested Price");
        costing.Range[heading - 2, 1].Text.Should().Be("Subtotal (base cost)");
        costing.Range[heading - 1, 1].Text.Should().BeNullOrEmpty("a blank row parts the comparison from the breakdown");
        Enumerable.Range(1, 3).Select(column => (costing.Range[heading, column].Text, costing.Range[heading, column].CellStyle.Font.Bold))
            .Should().Equal(("Component", true), ("Suggested Price", true), ("Current Price", true));
        Enumerable.Range(heading + 1, 3).Select(row => string.Join(" | ", Enumerable.Range(1, 3).Select(column => costing.Range[row, column].DisplayText)))
            .Should().Equal(
                $"Markup | (30.0%) {costs.Markup:N2} | ({costs.CurrentMarkupPercent:N1}%) {costs.CurrentMarkup:N2}",
                $"VAT (12%) | {costs.Vat:N2} | {costs.CurrentVat:N2}",
                $"Selling price per piece | {costs.SellingPrice:N2} | 10.00");
        costing.Range[heading + 1, 2].Number.Should().BeApproximately(decimal.ToDouble(costs.Markup), 1e-9, "the markup stays a number");
        workbook.Worksheets["Recipe"].Range["B2"].Text.Should().Be("Flour");
        workbook.Worksheets["Packaging"].Range["A2"].Text.Should().Be("Glassine");
    }

    private static TablePrintout Printout() => new(
        "Raw Materials Price Summary",
        ["Every purchased material's latest price.", "2 rows"],
        [new PrintedTable(null,
            [new("Raw Material"), new("Price", true), new("Since", KeepsToOneLine: true)],
            [["Flour", "1,200.00", "Sep 24, 2026"], ["Sugar", "2,500.00", "Oct 1, 2026"]])
        {
            Total = ["Total", "3,700.00", ""],
            Note = "Prices with VAT.",
        }],
        new DateTime(2026, 11, 19, 9, 0, 0));
}
