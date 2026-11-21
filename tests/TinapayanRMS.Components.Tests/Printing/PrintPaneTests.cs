using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>
/// The print preview's bar: Export to PDF saves the pages shown where the Save dialog says and says where, or why not;
/// Production copy holds Include to the recipe alone.
/// </summary>
public class PrintPaneTests
{
    private const string Chosen = @"C:\Exports\Breads.pdf";

    [Test]
    public async Task Export_to_PDF_saves_the_pages_shown_where_the_Save_dialog_says_and_says_where()
    {
        var (context, dialogs, window, notices) = NewContext();
        await using var _ = context;
        dialogs.SaveAnswer = Chosen;
        var pane = context.Render<PrintPane>(parameters => parameters.Add(p => p.Set, Set()));

        await pane.InvokeAsync(() => ButtonLabelled(pane, "Export to PDF").Click());

        await pane.WaitForAssertionAsync(() => window.PdfsSaved.Should().Equal(Chosen));
        dialogs.SavesAsked.Should().Equal(("Breads.pdf", "PDF document"));
        notices.Should().Contain((NoticeKind.Success, @"Saved Breads.pdf in C:\Exports."));
    }

    [Test]
    public async Task A_PDF_that_cannot_be_written_says_why_and_a_cancelled_one_writes_nothing()
    {
        var (context, dialogs, window, notices) = NewContext();
        await using var _ = context;
        var pane = context.Render<PrintPane>(parameters => parameters.Add(p => p.Set, Set()));

        await pane.InvokeAsync(() => ButtonLabelled(pane, "Export to PDF").Click());
        window.PdfsSaved.Should().BeEmpty("the dialog was cancelled");

        dialogs.SaveAnswer = Chosen;
        window.OnSavePdf = _ => throw new IOException("The file is open in another program.");
        await pane.InvokeAsync(() => ButtonLabelled(pane, "Export to PDF").Click());

        await pane.WaitForAssertionAsync(() => notices.Should().Contain((NoticeKind.Error, "The PDF wasn't saved: The file is open in another program.")));
    }

    [Test]
    public async Task Production_copy_prints_the_recipe_alone_whatever_Include_has()
    {
        var (context, _, _, _) = NewContext();
        await using var _1 = context;
        context.Services.GetRequiredService<PrintIncludes>().ProductionCopy = true;

        var pane = context.Render<PrintPane>(parameters => parameters.Add(p => p.Set, Set()));

        pane.FindAll(".print-production").Should().HaveCount(2, "each product says it's a production copy");
        pane.FindAll(".print-costing").Should().BeEmpty();
        pane.Find(".print-sheet .print-title p").TextContent.Should().Contain("Production Copy");
    }

    private static (BunitContext Context, FakeFileDialogs Dialogs, FakeAppWindow Window, List<(NoticeKind, string?)> Notices) NewContext()
    {
        var context = AppServices.NewContext();
        var dialogs = new FakeFileDialogs();
        context.Services.AddSingleton<IFileDialogs>(dialogs);
        List<(NoticeKind, string?)> notices = [];
        context.Services.GetRequiredService<Notices>().Changed += (kind, message) => notices.Add((kind, message));
        return (context, dialogs, context.Services.GetRequiredService<FakeAppWindow>(), notices);
    }

    private static AngleSharp.Dom.IElement ButtonLabelled(IRenderedComponent<PrintPane> pane, string label) =>
        pane.FindAll("button").Single(button => button.TextContent.Trim() == label);

    private static ProductPrintSet Set()
    {
        var printout = new ProductPrintout(
            "Pandesal", "Breads", "Production", "Per piece", "—", null, new DateTime(2026, 11, 9, 14, 0, 0),
            [new PrintedSection("Dough", null, "Baker's %", [new PrintedLine("Flour", 1000m, 100m, 0.052m, 52m)], [],
                1000m, 100m, 52m, 50m, 20m, 2.6m, true)],
            null,
            new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 2.6m)], MarkupPercent = 30m, VatPercent = 12m }));
        return new ProductPrintSet("Breads", [printout, printout with { ProductName = "Ensaymada" }], printout.PrintedAt);
    }
}
