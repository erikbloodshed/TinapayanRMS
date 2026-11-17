using AwesomeAssertions;
using TinapayanRMS.Components.Printing;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Printing;
using TinapayanRMS.Pricing;

namespace TinapayanRMS.Components.Tests.Printing;

/// <summary>
/// The printed costing: a product's breakdown ends at its base cost, with its suggested price beside its current one under
/// it, as the Costing tab shows them; the Price Simulator's report keeps its now-and-simulated rows in the breakdown.
/// </summary>
public class PrintDocumentCostingTests
{
    [Test]
    public async Task A_products_costing_puts_its_suggested_price_beside_its_current_one()
    {
        await using var context = AppServices.NewContext();

        // A base cost of 100.00 at a 30% markup and 12% VAT is 145.60; a current price of 134.40 is 120.00 before VAT.
        var document = Render(context, Printout(currentPrice: 134.40m));

        var breakdown = Rows(document, ".print-costing:not(.print-price-comparison)");
        breakdown.Should().EndWith("Subtotal (base cost) | 100.00 | 68.7%");
        breakdown.Should().NotContain(row => row.StartsWith("Markup", StringComparison.Ordinal) || row.StartsWith("VAT", StringComparison.Ordinal));
        Rows(document, ".print-price-comparison").Should().Equal(
            "Component | Suggested Price |  | Current Price | ",
            "Markup | 30.00 | (30.0%) | 20.00 | (20.0%)",
            "VAT (12%) | 15.60 |  | 14.40 | ",
            "Selling price | 145.60 |  | 134.40 | ",
            "Rounded up to the next 0.25 | 145.75 | ");
        document.FindAll(".print-price-comparison .print-warning").Select(cell => cell.TextContent).Should().Equal(
            ["20.00", "(20.0%)"], "the current price earns less than the 30% markup");
    }

    [Test]
    public async Task A_product_with_no_current_price_has_dashes_beside_its_suggested_price()
    {
        await using var context = AppServices.NewContext();

        var document = Render(context, Printout(currentPrice: null));

        Rows(document, ".print-price-comparison").Skip(1).Take(3).Should().Equal(
            "Markup | 30.00 | (30.0%) | — | ",
            "VAT (12%) | 15.60 |  | — | ",
            "Selling price | 145.60 |  | — | ");
        document.FindAll(".print-warning, .print-error").Should().BeEmpty();
    }

    [Test]
    public async Task A_simulations_costing_keeps_its_price_rows_in_the_breakdown()
    {
        await using var context = AppServices.NewContext();
        var printout = Printout(currentPrice: 134.40m) with { Simulation = new PrintedSimulation(Costs(currentPrice: 134.40m), []) };

        var document = Render(context, printout);

        document.FindAll(".print-price-comparison").Should().BeEmpty();
        Rows(document, ".print-costing").Should().Contain(row => row.StartsWith("Markup (30%)", StringComparison.Ordinal))
            .And.Contain(row => row.StartsWith("Suggested Selling Price per Piece", StringComparison.Ordinal));
    }

    private static IRenderedComponent<PrintDocument> Render(BunitContext context, ProductPrintout printout) =>
        context.Render<PrintDocument>(parameters => parameters
            .Add(d => d.Set, ProductPrintSet.Of(printout))
            .Add(d => d.IncludeRecipe, false));

    /// <summary>Each row of the tables <paramref name="selector"/> finds, its cells' text joined by " | ".</summary>
    private static List<string> Rows(IRenderedComponent<PrintDocument> document, string selector) =>
        [.. document.FindAll($"{selector} tr").Select(row => string.Join(" | ", row.Children.Select(cell => cell.TextContent.Trim())))];

    private static ProductPrintout Printout(decimal? currentPrice) =>
        new("Pandesal", "Breads", "Production", "Per piece", "—", null, new DateTime(2026, 11, 9, 14, 0, 0), [], null, Costs(currentPrice));

    /// <summary>One section at 100.00 a piece, at a 30% markup and 12% VAT, rounded up to the next 0.25.</summary>
    private static ProductCostSummary Costs(decimal? currentPrice) =>
        new(new ProductCostInputs
        {
            Parts = [("Dough", 100m)],
            MarginPercent = 30m,
            VatPercent = 12m,
            CurrentPrice = currentPrice,
            PriceRoundingStep = 0.25m,
        });
}
