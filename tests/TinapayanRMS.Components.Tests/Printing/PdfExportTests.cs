using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>Export to PDF: the Save dialog names the file for the product, and the pages are on the page only while the window writes them.</summary>
public class PdfExportTests
{
    private const string Chosen = @"C:\Exports\Pandesal.pdf";

    [Test]
    public async Task The_pages_are_drawn_while_the_window_writes_them_and_gone_after()
    {
        var (context, dialogs, window) = NewContext();
        await using var _ = context;
        dialogs.SaveAnswer = Chosen;
        var export = new Interaction<ProductPrintSet, string?>();
        var page = context.Render<PdfExport>(parameters => parameters.Add(p => p.Export, export));
        string? drawnAsSaved = null;
        window.OnSavePdf = _ =>
        {
            drawnAsSaved = page.Find(".pdf-export .print-document h1").TextContent;
            return Task.CompletedTask;
        };

        var saved = await page.InvokeAsync(() => export.Handle(Set("Pandesal")));

        saved.Should().Be(Chosen);
        dialogs.SavesAsked.Should().Equal(("Pandesal.pdf", "PDF document"));
        window.PdfsSaved.Should().Equal(Chosen);
        drawnAsSaved.Should().Be("Pandesal");
        await page.WaitForAssertionAsync(() => page.FindAll(".pdf-export").Should().BeEmpty());
    }

    [Test]
    public async Task Cancelling_the_Save_dialog_writes_nothing()
    {
        var (context, dialogs, window) = NewContext();
        await using var _ = context;
        dialogs.SaveAnswer = null;
        var export = new Interaction<ProductPrintSet, string?>();
        var page = context.Render<PdfExport>(parameters => parameters.Add(p => p.Export, export));

        var saved = await page.InvokeAsync(() => export.Handle(Set("Pandesal")));

        saved.Should().BeNull();
        window.PdfsSaved.Should().BeEmpty();
        page.FindAll(".pdf-export").Should().BeEmpty();
    }

    [Test]
    public async Task A_name_Windows_refuses_in_a_file_is_suggested_without_those_characters()
    {
        var (context, dialogs, _) = NewContext();
        await using var _1 = context;
        var export = new Interaction<ProductPrintSet, string?>();
        var page = context.Render<PdfExport>(parameters => parameters.Add(p => p.Export, export));

        await page.InvokeAsync(() => export.Handle(Set("Ube/Cheese: Roll?")));

        dialogs.SavesAsked.Single().FileName.Should().Be("Ube-Cheese- Roll-.pdf");
    }

    [Test]
    public async Task A_file_that_cannot_be_written_takes_the_pages_away_and_reaches_the_screen()
    {
        var (context, dialogs, window) = NewContext();
        await using var _ = context;
        dialogs.SaveAnswer = Chosen;
        window.OnSavePdf = _ => throw new IOException("The file is open in another program.");
        var export = new Interaction<ProductPrintSet, string?>();
        var page = context.Render<PdfExport>(parameters => parameters.Add(p => p.Export, export));

        var saving = () => page.InvokeAsync(() => export.Handle(Set("Pandesal")));

        await saving.Should().ThrowAsync<IOException>();
        await page.WaitForAssertionAsync(() => page.FindAll(".pdf-export").Should().BeEmpty());
    }

    private static (BunitContext Context, FakeFileDialogs Dialogs, FakeAppWindow Window) NewContext()
    {
        var context = AppServices.NewContext();
        var dialogs = new FakeFileDialogs();
        context.Services.AddSingleton<IFileDialogs>(dialogs);
        return (context, dialogs, context.Services.GetRequiredService<FakeAppWindow>());
    }

    private static ProductPrintSet Set(string productName) => ProductPrintSet.Of(new ProductPrintout(
        productName, "Breads", "Production", "Per piece", "—", null, new DateTime(2026, 11, 9, 14, 0, 0), [], null,
        new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 100m)], MarginPercent = 30m, VatPercent = 12m })));
}
