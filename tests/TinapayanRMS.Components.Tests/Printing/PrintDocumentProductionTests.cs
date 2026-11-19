using AwesomeAssertions;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>
/// A draft printout says it is one, and a production copy is the recipe alone in large type with a box to tick beside
/// each line and a line to sign.
/// </summary>
public class PrintDocumentProductionTests
{
    [Test]
    public async Task A_printout_with_unsaved_changes_is_marked_as_a_draft()
    {
        await using var context = AppServices.NewContext();

        var draft = Render(context, Printout(ProductPrintout.UnsavedChangesNote));

        draft.Find(".print-draft").TextContent.Should().Be(
            "Draft: it includes changes not yet saved. Save it on the Products screen before it's used.");
        draft.FindAll(".print-draft-mark").Should().ContainSingle("the faint Draft across each printed page");
    }

    [Test]
    public async Task A_product_never_saved_is_a_draft_but_one_as_saved_or_from_the_history_is_not()
    {
        await using var context = AppServices.NewContext();

        Render(context, Printout(ProductPrintout.NotSavedNote)).Find(".print-draft").TextContent.Should().StartWith("Draft: this product hasn't been saved yet.");
        Render(context, Printout(null)).FindAll(".print-draft, .print-draft-mark").Should().BeEmpty();
        Render(context, Printout(ProductPrintout.VersionNote)).FindAll(".print-draft, .print-draft-mark").Should().BeEmpty();
    }

    [Test]
    public async Task A_production_copy_has_a_box_to_tick_on_each_line_large_type_and_lines_to_sign()
    {
        await using var context = AppServices.NewContext();

        var copy = context.Render<PrintDocument>(parameters => parameters
            .Add(d => d.Set, ProductPrintSet.Of(Printout(null)))
            .Add(d => d.IncludeCosting, false)
            .Add(d => d.ProductionCopy, true));

        copy.Find(".print-block thead tr").Children.Select(cell => cell.TextContent.Trim()).Should().Equal("Ingredient", "Grams", "Baker's %", "Done");
        copy.FindAll(".print-block tbody .print-tick-box").Should().HaveCount(2, "one for each ingredient");
        copy.Find(".print-block tfoot tr").Children.Should().HaveCount(4, "the total lines up under the box's column");
        copy.Find(".print-production").TextContent.Should().Be("Production copy");
        copy.Find(".print-signoff").TextContent.Should().Contain("Weighed by").And.Contain("Checked by").And.Contain("Date");
        copy.Find(".print-document").GetAttribute("style").Should().EndWith("--type-scale: 1.25");
        copy.FindAll(".print-costing, .print-figures").Should().BeEmpty();
    }

    private static IRenderedComponent<PrintDocument> Render(BunitContext context, ProductPrintout printout) =>
        context.Render<PrintDocument>(parameters => parameters.Add(d => d.Set, ProductPrintSet.Of(printout)));

    private static ProductPrintout Printout(string? saveNote) => new(
        "Pandesal", "Breads", "Production", "Per piece", "—", saveNote, new DateTime(2026, 11, 9, 14, 0, 0),
        [new PrintedSection("Dough", null, "Baker's %",
            [new PrintedLine("Flour", 1000m, 100m, 0.052m, 52m), new PrintedLine("Water", 600m, 60m, 0.001m, 0.6m)], [],
            1600m, 160m, 52.6m, 50m, 32m, 1.64m, true)],
        null,
        new ProductCostSummary(new ProductCostInputs { Parts = [("Dough", 1.64m)], MarginPercent = 30m, VatPercent = 12m }));
}
