using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Sections;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
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
        var page = RenderWithOutlet(context, export);
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
    public async Task The_pages_are_laid_out_on_the_paper_Settings_has_saved()
    {
        var (context, dialogs, window) = NewContext();
        await using var _ = context;
        dialogs.SaveAnswer = Chosen;
        context.Services.GetRequiredService<InMemoryPrintSettings>().Saved = new PrintSettings
        {
            PaperSize = PaperSize.Letter, Orientation = PaperOrientation.Landscape, Margins = PageMargins.Narrow,
        };
        var export = new Interaction<ProductPrintSet, string?>();
        var page = RenderWithOutlet(context, export);
        (string Rule, string? Height)? drawn = null;
        window.OnSavePdf = _ =>
        {
            drawn = (page.Find(".pdf-export style").TextContent, page.Find(".pdf-export .print-document").GetAttribute("data-page-height"));
            return Task.CompletedTask;
        };

        await page.InvokeAsync(() => export.Handle(Set("Pandesal")));

        drawn.Should().NotBeNull();
        drawn!.Value.Rule.Should().Be("@page { size: 279.4mm 215.9mm; margin: 8mm 8mm 12mm; }", "Letter turned on its side, with narrow margins");
        drawn.Value.Height.Should().Be("195.9", "215.9 mm less the 8 mm top and 12 mm foot");
    }

    [Test]
    public async Task Cancelling_the_Save_dialog_writes_nothing()
    {
        var (context, dialogs, window) = NewContext();
        await using var _ = context;
        dialogs.SaveAnswer = null;
        var export = new Interaction<ProductPrintSet, string?>();
        var page = RenderWithOutlet(context, export);

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
        var page = RenderWithOutlet(context, export);

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
        var page = RenderWithOutlet(context, export);

        var saving = () => page.InvokeAsync(() => export.Handle(Set("Pandesal")));

        await saving.Should().ThrowAsync<IOException>();
        await page.WaitForAssertionAsync(() => page.FindAll(".pdf-export").Should().BeEmpty());
    }

    [Test]
    public async Task The_pages_are_drawn_in_the_layouts_outlet_outside_the_screen()
    {
        var (context, dialogs, window) = NewContext();
        await using var _ = context;
        dialogs.SaveAnswer = Chosen;
        var export = new Interaction<ProductPrintSet, string?>();
        var page = RenderWithOutlet(context, export);
        bool? inOutlet = null;
        window.OnSavePdf = _ =>
        {
            inOutlet = page.Find(".pdf-export").ParentElement?.ClassName == "outlet";
            return Task.CompletedTask;
        };

        await page.InvokeAsync(() => export.Handle(Set("Pandesal")));

        inOutlet.Should().BeTrue("print.css prints the pages alone only outside the window's shell, where MainLayout's outlet is");
    }

    // PdfExport in a screen, with MainLayout's outlet its pages are drawn in, apart from it.
    private static IRenderedComponent<ExportHost> RenderWithOutlet(BunitContext context, Interaction<ProductPrintSet, string?> export) =>
        context.Render<ExportHost>(parameters => parameters.Add(p => p.Export, export));

    private sealed class ExportHost : ComponentBase
    {
        [Parameter] public Interaction<ProductPrintSet, string?> Export { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "div");
            builder.AddAttribute(1, "class", "outlet");
            builder.OpenComponent<SectionOutlet>(2);
            builder.AddComponentParameter(3, nameof(SectionOutlet.SectionName), PdfExport.OutletName);
            builder.CloseComponent();
            builder.CloseElement();
            builder.OpenElement(4, "div");
            builder.AddAttribute(5, "class", "screen");
            builder.OpenComponent<PdfExport>(6);
            builder.AddComponentParameter(7, nameof(PdfExport.Export), Export);
            builder.CloseComponent();
            builder.CloseElement();
        }
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
        new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 100m)], MarkupPercent = 30m, VatPercent = 12m })));
}
