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
/// figure tiles and the price comparison; the print preview's Include ticks, as the Windows user last left them; and
/// Include's sub-assembly total, in place of the costing.
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
        context.Services.AddSingleton<Components.Shared.IFileDialogs>(new FakeFileDialogs());
        context.Services.GetRequiredService<IViewStateStore>().WriteList(PrintIncludes.Key, ["recipe"]);

        var pane = context.Render<PrintPane>(parameters => parameters.Add(p => p.Set, Set()));

        pane.FindAll(".print-costing").Should().BeEmpty("the costing was cleared last time");
        Headings(pane, ".print-block table").Should().Equal("Ingredient", "Grams", "Baker's %");
    }

    [Test]
    public async Task The_sub_assembly_total_prints_the_recipes_costs_with_unit_prices_and_the_total_at_the_end()
    {
        await using var context = AppServices.NewContext();
        context.Services.GetRequiredService<PrintPage>().Use(new PrintSettings { OmitUnitCosts = true });
        var packaging = new PrintedPackaging(null, [new PrintedPackagingLine("Paper Bag", 1m, "pc", 0.75m, 0.75m)], 0.75m);

        var document = context.Render<PrintDocument>(parameters => parameters
            .Add(d => d.Set, ProductPrintSet.Of(Set().Products[0] with { Packaging = packaging }))
            .Add(d => d.IncludeCosting, false)
            .Add(d => d.IncludeSubAssemblyTotal, true));

        Headings(document, ".print-block table").Should().Equal(["Ingredient", "Grams", "Baker's %", "Cost/g", "Cost"], "it has the unit prices whatever Settings leaves out");
        Row(document, ".print-block tbody tr").Should().Equal("Flour", "1,000.0", "100.0", "0.0520", "52.00");
        Row(document, ".print-block tfoot tr").Should().Equal("Total", "1,000.0", "100.0", "", "52.00");
        document.Find(".print-block .print-small").TextContent.Should().EndWith("Cost per piece 2.60");
        var packagingTable = document.FindAll(".print-block table")[1];
        packagingTable.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()).Should().Equal("Material", "Qty", "Unit", "Unit Cost", "Cost");
        packagingTable.QuerySelector("tbody tr")!.Children.Select(cell => cell.TextContent.Trim()).Should().Equal("Paper Bag", "1", "pc", "0.7500", "0.75");
        packagingTable.QuerySelector("tfoot tr")!.Children.Select(cell => cell.TextContent.Trim()).Should().Equal("Total", "", "", "", "0.75");
        Row(document, ".print-sub-assembly-total tr").Should().Equal("Total sub-assembly cost (Per piece)", "2.60");
        document.FindAll(".print-figures, .print-price-comparison, .print-part-row").Should().BeEmpty("none of the costing's breakdown prints");
        document.FindAll(".print-costing").Should().ContainSingle("the total alone, not the breakdown");
    }

    [Test]
    public async Task The_bulk_packaging_prints_under_the_packaging_with_the_packs_cost_and_each_products_share()
    {
        await using var context = AppServices.NewContext();
        var bulk = new PrintedBulkPackaging([new PrintedPackagingLine("Box of 6", 1m, "pc", 6m, 6m)], 6, 6m, 1m);
        var packaging = new PrintedPackaging(null, [new PrintedPackagingLine("Paper Bag", 1m, "pc", 0.75m, 0.75m)], 0.75m) { Bulk = bulk };

        var document = context.Render<PrintDocument>(parameters => parameters
            .Add(d => d.Set, ProductPrintSet.Of(Set().Products[0] with { Packaging = packaging })));

        var packagingBlock = document.FindAll(".print-block")[1];
        packagingBlock.QuerySelectorAll("h3").Select(heading => heading.TextContent.Trim())
            .Should().Equal("Packaging", "Bulk Packaging one pack holds 6 products");
        var bulkTable = packagingBlock.QuerySelectorAll("table")[1];
        bulkTable.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim())
            .Should().Equal("Material", "Qty per Pack", "Unit", "Unit Cost", "Cost per Pack");
        bulkTable.QuerySelector("tbody tr")!.Children.Select(cell => cell.TextContent.Trim())
            .Should().Equal("Box of 6", "1", "pc", "6.0000", "6.00");
        var totals = bulkTable.QuerySelectorAll("tfoot tr");
        totals.Should().HaveCount(2);
        totals[0].Children.Select(cell => cell.TextContent.Trim()).Should().Equal("Pack cost", "", "", "", "6.00");
        totals[1].Children.Select(cell => cell.TextContent.Trim()).Should().Equal("Per unit", "", "", "", "1.00");
    }
    [Test]
    public async Task The_costing_wins_over_the_sub_assembly_total_and_a_production_copy_has_neither()
    {
        await using var context = AppServices.NewContext();
        context.Services.AddSingleton<Components.Shared.IFileDialogs>(new FakeFileDialogs());
        var includes = context.Services.GetRequiredService<PrintIncludes>();
        includes.SubAssemblyTotal = true;

        var pane = context.Render<PrintPane>(parameters => parameters.Add(p => p.Set, Set()));
        pane.FindAll(".print-sub-assembly-total").Should().ContainSingle();

        includes.ProductionCopy = true;
        pane.Render();
        pane.FindAll(".print-sub-assembly-total, .print-costing").Should().BeEmpty();
        Headings(pane, ".print-block table").Should().NotContain("Cost");

        includes.ProductionCopy = false;
        includes.Costing = true;
        pane.Render();
        pane.FindAll(".print-sub-assembly-total").Should().BeEmpty();
        pane.FindAll(".print-price-comparison").Should().ContainSingle("the whole costing prints");
    }

    private static IRenderedComponent<PrintDocument> Render(BunitContext context) =>
        context.Render<PrintDocument>(parameters => parameters.Add(d => d.Set, Set()));

    private static ProductPrintSet Set() => ProductPrintSet.Of(new ProductPrintout(
        "Pandesal", "Breads", "Production", "Per piece", "—", null, new DateTime(2026, 11, 9, 14, 0, 0),
        [new PrintedSection("Dough", null, "Baker's %", [new PrintedLine("Flour", 1000m, 100m, 0.052m, 52m)], [],
            1000m, 100m, 52m, 50m, 20m, 2.6m, true)],
        null,
        new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 2.6m)], MarkupPercent = 30m, VatPercent = 12m })));

    private static List<string> Headings<T>(IRenderedComponent<T> rendered, string table) where T : IComponent =>
        [.. rendered.Find(table).QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim())];

    private static List<string> Row<T>(IRenderedComponent<T> rendered, string row) where T : IComponent =>
        [.. rendered.Find(row).Children.Select(cell => cell.TextContent.Trim())];
}
