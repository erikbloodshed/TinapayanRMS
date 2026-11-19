using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>
/// The page a printout is laid out on: Settings → Printing's paper and margins in its @page rule, its sheets on the screen
/// as wide as the paper, and the height app.js fills each page to; the shop's heading at the top of each part, the note at
/// each page's foot, and the type's size.
/// </summary>
public class PrintDocumentPageTests
{
    [Test]
    public async Task Before_Settings_is_read_the_page_is_A4_with_normal_margins()
    {
        await using var context = AppServices.NewContext();

        var document = Render(context);

        document.Find("style").TextContent.Should().Be("@page { size: 210mm 297mm; margin: 14mm 14mm 16mm; }");
        var printout = document.Find(".print-document");
        printout.GetAttribute("data-page-height").Should().Be("267");
        printout.GetAttribute("style").Should().Be(
            "--paper-width: 210mm; --paper-height: 297mm; --margin-top: 14mm; --margin-side: 14mm; --margin-bottom: 16mm; --type-scale: 1");
        document.FindAll(".print-shop").Should().BeEmpty("there's no heading until Settings has one");
    }

    [Test]
    public async Task The_shop_heading_starts_each_part_and_the_note_is_at_each_page_foot()
    {
        await using var context = AppServices.NewContext();
        context.Services.GetRequiredService<PrintPage>().Use(new PrintSettings
        {
            ShopName = "Tinapayan Festival Bakeshoppe", ShopAddress = "Poblacion, Tagum City",
            Logo = [0x89, 0x50, 0x4E, 0x47], LogoContentType = "image/png",
            FooterNote = "For \"internal\" use only", TypeSize = PrintTypeSize.Large,
        });

        var document = Render(context);

        var heading = document.Find(".print-product > .print-shop");
        heading.QuerySelector(".print-shop-name")!.TextContent.Should().Be("Tinapayan Festival Bakeshoppe");
        heading.QuerySelector(".print-shop-address")!.TextContent.Should().Be("Poblacion, Tagum City");
        heading.QuerySelector("img")!.GetAttribute("src").Should().Be("data:image/png;base64,iVBORw==");
        document.Find("style").TextContent.Should().Contain(
            "@bottom-left { content: \"For \\\"internal\\\" use only\";", "the note is a CSS string, its quotes escaped");
        document.Find(".print-document").GetAttribute("style").Should().EndWith("--type-scale: 1.2");
    }

    [Test]
    public async Task Long_bond_in_landscape_with_wide_margins_is_the_page_each_part_is_laid_out_on()
    {
        await using var context = AppServices.NewContext();
        context.Services.GetRequiredService<PrintPage>().Use(
            new PrintSettings { PaperSize = PaperSize.LongBond, Orientation = PaperOrientation.Landscape, Margins = PageMargins.Wide });

        var document = Render(context);

        document.Find("style").TextContent.Should().Be("@page { size: 330.2mm 215.9mm; margin: 20mm 20mm 22mm; }");
        var printout = document.Find(".print-document");
        printout.GetAttribute("data-page-height").Should().Be("173.9");
        printout.GetAttribute("style").Should().StartWith("--paper-width: 330.2mm; --paper-height: 215.9mm; --margin-top: 20mm;");
    }

    private static IRenderedComponent<PrintDocument> Render(BunitContext context) =>
        context.Render<PrintDocument>(parameters => parameters.Add(d => d.Set, ProductPrintSet.Of(new ProductPrintout(
            "Pandesal", "Breads", "Production", "Per piece", "—", null, new DateTime(2026, 11, 9, 14, 0, 0), [], null,
            new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 100m)], MarginPercent = 30m, VatPercent = 12m })))));
}
