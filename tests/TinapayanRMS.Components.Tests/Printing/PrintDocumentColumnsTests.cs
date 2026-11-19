using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Domain.Entities;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>
/// What Settings → Printing leaves out of a printout: the recipe's unit costs and percentages, column and cell alike, the
/// figure tiles and the price comparison; and the print preview's Include ticks, as the Windows user last left them.
/// </summary>
public class PrintDocumentColumnsTests
{
    [Test]
    public async Task Everything_prints_until_Settings_leaves_something_out()
    {
        await using var context = AppServices.NewContext();

        var document = Render(context);

        Headings(document, ".print-block table").Should().Equal("Ingredient", "Grams", "Baker's %", "Cost/g", "Cost");
        Row(document, ".print-block tfoot tr").Should().Equal("Total", "1,000.0", "100.0", "", "52.00");
        document.FindAll(".print-figures").Should().ContainSingle();
        document.FindAll(".print-price-comparison").Should().ContainSingle();
    }

    [Test]
    public async Task Unit_costs_percentages_figures_and_the_price_comparison_can_each_be_left_out()
    {
        await using var context = AppServices.NewContext();
        context.Services.GetRequiredService<PrintPage>().Use(new PrintSettings
        {
            OmitUnitCosts = true, OmitPercentages = true, OmitFigures = true, OmitPriceComparison = true,
        });

        var document = Render(context);

        Headings(document, ".print-block table").Should().Equal("Ingredient", "Grams", "Cost");
        Row(document, ".print-block tbody tr").Should().Equal("Flour", "1,000.0", "52.00");
        Row(document, ".print-block tfoot tr").Should().Equal("Total", "1,000.0", "52.00");
        document.FindAll(".print-figures").Should().BeEmpty();
        document.FindAll(".print-price-comparison").Should().BeEmpty();
        document.FindAll(".print-costing").Should().ContainSingle("the costing's breakdown still prints");
    }

    [Test]
    public async Task The_preview_starts_with_the_Include_ticks_as_they_were_left()
    {
        await using var context = AppServices.NewContext();
        context.Services.GetRequiredService<IViewStateStore>().WriteList(PrintIncludes.Key, ["recipe"]);

        var pane = context.Render<PrintPane>(parameters => parameters.Add(p => p.Set, Set()));

        pane.FindAll(".print-costing").Should().BeEmpty("the costing was cleared last time");
        Headings(pane, ".print-block table").Should().Equal("Ingredient", "Grams", "Baker's %");
    }

    private static IRenderedComponent<PrintDocument> Render(BunitContext context) =>
        context.Render<PrintDocument>(parameters => parameters.Add(d => d.Set, Set()));

    private static ProductPrintSet Set() => ProductPrintSet.Of(new ProductPrintout(
        "Pandesal", "Breads", "Production", "Per piece", "—", null, new DateTime(2026, 11, 9, 14, 0, 0),
        [new PrintedSection("Dough", null, "Baker's %", [new PrintedLine("Flour", 1000m, 100m, 0.052m, 52m)], [],
            1000m, 100m, 52m, 50m, 20m, 2.6m, true)],
        null,
        new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 2.6m)], MarginPercent = 30m, VatPercent = 12m })));

    private static List<string> Headings<T>(IRenderedComponent<T> rendered, string table) where T : IComponent =>
        [.. rendered.Find(table).QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim())];

    private static List<string> Row<T>(IRenderedComponent<T> rendered, string row) where T : IComponent =>
        [.. rendered.Find(row).Children.Select(cell => cell.TextContent.Trim())];
}
