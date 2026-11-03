using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AwesomeAssertions;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Domain.Enums;
using TinapayanRMS.Pricing;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Tests.Printing;

/// <summary>ProductPrintLayout: a printout's pages, built as the print pane and the printer are handed them.</summary>
public class ProductPrintLayoutTests
{
    private static readonly Size A4 = SamplePrintouts.A4Printable;

    [Fact]
    public void A_recipe_too_long_for_a_page_runs_on_under_its_heading_and_header_row_again() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(SamplePrintouts.WithLines(80), includeRecipe: true, includeCosting: true, A4);

        layout.PageCount.Should().BeGreaterThan(1);
        var body = Body(layout.BuildPage(2));
        Texts(body[0]).Single().Should().StartWith("Dough (continued)");
        Texts(body[1]).Should().Equal("Ingredient", "Grams", "Baker's %", "Cost/g", "Cost", "Base");
    });

    [Fact]
    public void A_sections_lines_show_their_cost_per_gram_and_the_packagings_dont() => StaThread.Run(() =>
    {
        var texts = AllTexts(new ProductPrintLayout(SamplePrintouts.WithLines(1), includeRecipe: true, includeCosting: true, A4));

        texts.Should().ContainInConsecutiveOrder("Ingredient 1", Figure(100m, "N1"), Figure(10m, "N1"), Figure(0.05m, "N4"), Figure(5m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Material", "Qty", "Unit", "Unit Cost", "Cost");
    });

    [Fact]
    public void An_unbuffered_printout_says_so_under_its_title_and_over_its_costing_and_a_buffered_one_doesnt() => StaThread.Run(() =>
    {
        var buffered = AllTexts(new ProductPrintLayout(SamplePrintouts.WithLines(1), includeRecipe: true, includeCosting: true, A4));
        var unbuffered = AllTexts(new ProductPrintLayout(
            SamplePrintouts.WithLines(1) with { CostBasis = CostBasis.Unbuffered }, includeRecipe: true, includeCosting: true, A4));

        buffered.Should().NotContain(t => t.Contains(ProductPrintout.UnbufferedNote, StringComparison.Ordinal));
        unbuffered.Should().Contain(t => t.EndsWith($" · {ProductPrintout.UnbufferedNote}", StringComparison.Ordinal) && t.Contains("Per piece", StringComparison.Ordinal));
        unbuffered.Should().Contain(t => t.StartsWith("Costing (", StringComparison.Ordinal) && t.Contains(ProductPrintout.UnbufferedNote, StringComparison.Ordinal));
    });

    [Fact]
    public void Without_the_costing_the_recipe_leaves_out_every_cost() => StaThread.Run(() =>
    {
        var texts = AllTexts(new ProductPrintLayout(SamplePrintouts.WithLines(2), includeRecipe: true, includeCosting: false, A4));

        texts.Should().ContainInConsecutiveOrder("Ingredient", "Grams", "Baker's %", "Base");
        texts.Should().ContainInConsecutiveOrder("Ingredient 1", Figure(100m, "N1"), Figure(10m, "N1"));
        texts.Should().ContainInConsecutiveOrder("Material", "Qty", "Unit", "Gift Box", Figure(1m, "N1"), "pc");
        texts.Should().NotContain(t => t.Contains("Cost", StringComparison.Ordinal), "no cost heading or cost per piece");
        texts.Should().NotContain([Figure(0.05m, "N4"), Figure(5m, "N2"), Figure(10m, "N2"), Figure(30m, "N4"), Figure(30m, "N2")], "no line's, batch's or box's cost");
    });

    [Fact]
    public void A_composites_recipe_is_its_products_table_and_its_costing_calls_them_products() => StaThread.Run(() =>
    {
        var texts = AllTexts(new ProductPrintLayout(SamplePrintouts.Composite(), includeRecipe: true, includeCosting: true, A4));

        texts.Should().ContainInConsecutiveOrder("Products", "Product", "Qty", "Recipe Cost", "Cost");
        texts.Should().ContainInConsecutiveOrder("Ensaymada", Figure(6m, "N0"), Figure(2.50m, "N2"), Figure(15m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Cheese Roll", Figure(4m, "N0"), Figure(5m, "N2"), Figure(20m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Total", string.Empty, string.Empty, Figure(35m, "N2"));
        texts.Should().Contain("Products (ingredients)");
        texts.Should().Contain(t => t.StartsWith("Labor (25% of products)", StringComparison.Ordinal));
        texts.Should().NotContain("No sub-assemblies yet.");
        texts.Should().NotContain("Sub-assemblies (ingredients)");
    });

    [Fact]
    public void A_composite_including_its_products_packaging_prints_a_Packaging_Cost_column_and_names_its_packaging_row() => StaThread.Run(() =>
    {
        var printout = SamplePrintouts.Composite() with
        {
            Components = [new PrintedComponent("Ensaymada", 6, 2.50m, 24m, 1.50m), new PrintedComponent("Cheese Roll", 4, 5m, 20m, 0m)],
            Costs = new ProductCostSummary(
                [("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m)], 1, [30m], 25m, 10m, 5m, 30m, 12m, 120m, isComposite: true, componentPackaging: 9m),
        };

        var texts = AllTexts(new ProductPrintLayout(printout, includeRecipe: true, includeCosting: true, A4));

        texts.Should().ContainInConsecutiveOrder("Products", "Product", "Qty", "Recipe Cost", "Packaging Cost", "Cost");
        texts.Should().ContainInConsecutiveOrder("Ensaymada", Figure(6m, "N0"), Figure(2.50m, "N2"), Figure(1.50m, "N2"), Figure(24m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Total", string.Empty, string.Empty, string.Empty, Figure(44m, "N2"));
        texts.Should().Contain("Packaging (own and products')");

        // Without the costing, neither cost column prints.
        var kitchen = AllTexts(new ProductPrintLayout(printout, includeRecipe: true, includeCosting: false, A4));
        kitchen.Should().NotContain("Packaging Cost");
    });

    [Fact]
    public void A_composites_own_topping_prints_after_its_products_and_its_costing_names_both() => StaThread.Run(() =>
    {
        var composite = SamplePrintouts.Composite();
        var topping = SamplePrintouts.WithLines(1).Sections.Single() with { Name = "Topping", Source = null };
        var printout = composite with
        {
            Sections = [topping],
            Costs = new ProductCostSummary(
                [("Ensaymada × 6", 15m), ("Cheese Roll × 4", 20m), ("Topping", 2.5m)], 1, [30m], 25m, 10m, 5m, 30m, 12m, 120m, isComposite: true, hasSections: true),
        };

        var texts = AllTexts(new ProductPrintLayout(printout, includeRecipe: true, includeCosting: true, A4));

        texts.IndexOf("Topping").Should().BeGreaterThan(texts.IndexOf("Cheese Roll"), "the products come first, then its own sections");
        texts.Should().Contain("Products and sub-assemblies (ingredients)");
        texts.Should().Contain(t => t.StartsWith("Labor (25% of products and sub-assemblies)", StringComparison.Ordinal));
    });

    [Fact]
    public void Without_the_costing_a_composites_products_table_has_no_costs_and_an_empty_one_says_so() => StaThread.Run(() =>
    {
        var texts = AllTexts(new ProductPrintLayout(SamplePrintouts.Composite(), includeRecipe: true, includeCosting: false, A4));

        texts.Should().ContainInConsecutiveOrder("Product", "Qty", "Ensaymada", Figure(6m, "N0"), "Cheese Roll", Figure(4m, "N0"));
        texts.Should().NotContain("Recipe Cost");
        texts.Should().NotContain(Figure(15m, "N2"));

        var empty = AllTexts(new ProductPrintLayout(SamplePrintouts.Composite() with { Components = [] }, includeRecipe: true, includeCosting: true, A4));
        empty.Should().Contain("No products yet.");
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Every_pages_blocks_fit_above_its_footer(bool landscape) => StaThread.Run(() =>
    {
        var size = landscape ? new Size(A4.Height, A4.Width) : A4;
        var layout = new ProductPrintLayout(SamplePrintouts.WithLines(80), includeRecipe: true, includeCosting: true, size);

        EveryPageShouldFitAboveItsFooter(layout, size);
    });

    [Fact]
    public void A_long_name_runs_the_footer_on_to_a_second_line_and_the_pages_still_fit_above_it() => StaThread.Run(() =>
    {
        var printout = SamplePrintouts.WithLines(80, ProductPrintout.UnsavedChangesNote);
        var usual = new ProductPrintLayout(printout, includeRecipe: true, includeCosting: true, A4);
        var named = new ProductPrintLayout(
            printout with { ProductName = "Mini Ube-Cheese Ensaymada with Salted Egg and Queso de Bola, Gift Box of 12" },
            includeRecipe: true, includeCosting: true, A4);

        Footer(LaidOut(named, 1, A4)).DesiredSize.Height.Should().BeGreaterThan(
            Footer(LaidOut(usual, 1, A4)).DesiredSize.Height, "the time it was printed wraps beside the save note rather than being cut off");
        EveryPageShouldFitAboveItsFooter(named, A4);
    });

    [Fact]
    public void The_footer_says_what_was_printed_and_when_whether_its_what_was_saved_and_which_page() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(
            SamplePrintouts.WithLines(3, ProductPrintout.UnsavedChangesNote), includeRecipe: true, includeCosting: false, A4);
        var printed = new DateTime(2026, 9, 27, 15, 40, 0).ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture);

        Texts(Footer((Grid)layout.BuildPage(1))).Should().Equal(
            $"Ube Roll — Recipe · Printed {printed}",
            "Includes unsaved changes   Page 1 of 1");
    });

    [Fact]
    public void A_sets_contents_page_lists_each_product_with_the_page_it_starts_on() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(
            SamplePrintouts.Set("Breads · Production", ("Cheese Roll", 3), ("Pan de Coco", 80), ("Ube Roll", 3)), includeRecipe: true, includeCosting: true, A4);

        var contents = AllTexts(layout, 1);
        contents[0].Should().Be("Breads · Production");
        contents.Should().Contain(t => t.StartsWith("3 products · Recipe and Costing · Printed", StringComparison.Ordinal));
        contents.Should().ContainInConsecutiveOrder("Product", "Folder", "Page");
        string[] names = ["Cheese Roll", "Pan de Coco", "Ube Roll"];
        for (var i = 0; i < names.Length; i++)
        {
            var start = layout.StartPageOf(i);
            contents.Should().ContainInConsecutiveOrder(names[i], "Production · Breads", start.ToString(CultureInfo.CurrentCulture));
            Texts(Body(layout.BuildPage(start))[0]).First().Should().Be(names[i], $"{names[i]} starts page {start} with its title");
        }

        layout.StartPageOf(0).Should().Be(2, "the contents come first");
        layout.StartPageOf(2).Should().BeGreaterThan(layout.StartPageOf(1) + 1, "Pan de Coco runs on to a second page");
        layout.DocumentName.Should().Be("Breads · Production — Recipe and Costing");
    });

    [Fact]
    public void A_sets_footers_name_each_pages_product_and_count_the_pages_over_the_whole_document() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(
            SamplePrintouts.Set("All Products", ("Cheese Roll", 3), ("Ube Roll", 3)), includeRecipe: true, includeCosting: false, A4);
        var printed = new DateTime(2026, 9, 27, 15, 40, 0).ToString("MMM d, yyyy, h:mm tt", CultureInfo.CurrentCulture);

        layout.PageCount.Should().Be(3);
        Texts(Footer((Grid)layout.BuildPage(1))).Should().Equal($"All Products — Contents · Printed {printed}", "Page 1 of 3");
        Texts(Footer((Grid)layout.BuildPage(3))).Should().Equal($"Ube Roll — Recipe · Printed {printed}", "Page 3 of 3");
    });

    [Fact]
    public void An_empty_folder_prints_one_page_saying_so() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(SamplePrintouts.Set("Cakes · Production"), includeRecipe: true, includeCosting: true, A4);

        layout.PageCount.Should().Be(1);
        AllTexts(layout).Should().Contain("No products in Cakes · Production.").And.NotContain("Product");
    });

    [Fact]
    public void A_single_product_has_no_contents_page() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(SamplePrintouts.WithLines(3), includeRecipe: true, includeCosting: true, A4);

        Texts(Body(layout.BuildPage(1))[0]).First().Should().Be("Ube Roll");
        layout.StartPageOf(0).Should().Be(1);
    });

    [Fact]
    public void A_part_left_out_is_nowhere_on_the_pages() => StaThread.Run(() =>
    {
        var recipe = AllTexts(new ProductPrintLayout(SamplePrintouts.WithLines(3), includeRecipe: true, includeCosting: false, A4));
        var costing = AllTexts(new ProductPrintLayout(SamplePrintouts.WithLines(3), includeRecipe: false, includeCosting: true, A4));

        recipe.Should().Contain("Recipe").And.Contain("Ingredient 1").And.NotContain(t => t.StartsWith("Costing", StringComparison.Ordinal));
        costing.Should().Contain("Costing (Per piece)").And.Contain("Total Selling Price (Per piece)").And.NotContain("Recipe").And.NotContain("Ingredient 1");
    });

    [Fact]
    public void A_scaled_recipe_prints_the_Scaling_tabs_rows_in_place_of_the_recipe_and_the_costing() => StaThread.Run(() =>
    {
        // Both parts ticked: the Scaling tab prints in place of them whatever is ticked.
        var layout = new ProductPrintLayout(SamplePrintouts.Scaled(), includeRecipe: true, includeCosting: true, A4);
        var texts = AllTexts(layout);

        texts.Should().ContainInConsecutiveOrder("Scaling", "Sellable Units", "20", "Pieces", "160", "Total Cost", Figure(708m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Item", "Amount", "Cost");
        texts.Should().ContainInConsecutiveOrder("Dough   2 batches · 200 pcs (40 extra)", "2,000 g", Figure(108m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Syrup", "200 g", Figure(18m, "N2"), "Sugar", "200 g", Figure(18m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Gift Box", "20 pc", Figure(600m, "N2"));
        texts.Count(t => t == Figure(708m, "N2")).Should().Be(1, "the total is the figure at the top, as on the tab, with no total row repeating it");
        texts.Should().NotContain("Recipe").And.NotContain(t => t.StartsWith("Costing", StringComparison.Ordinal)).And.NotContain("Ingredient 1");
        layout.DocumentName.Should().Be("Ube Roll — Scaling");
        Texts(Footer((Grid)layout.BuildPage(1))).First().Should().StartWith("Ube Roll — Scaling · Printed ");
    });

    [Fact]
    public void A_scaled_recipes_rows_sit_in_under_the_row_theyre_part_of() => StaThread.Run(() =>
    {
        var body = Body(new ProductPrintLayout(SamplePrintouts.Scaled(), includeRecipe: true, includeCosting: true, A4).BuildPage(1));

        double IndentOf(string name) =>
            body.Select(block => block is Border { Child: Grid row } ? row.Children.OfType<TextBlock>().FirstOrDefault() : null)
                .Single(item => item is not null && new TextRange(item.ContentStart, item.ContentEnd).Text.StartsWith(name, StringComparison.Ordinal))!
                .Margin.Left;

        IndentOf("Flour").Should().BeGreaterThan(IndentOf("Dough"));
        IndentOf("Sugar").Should().BeGreaterThan(IndentOf("Syrup"), "a made ingredient opens into what it's made from");
        IndentOf("Syrup").Should().Be(IndentOf("Flour"));
        IndentOf("Packaging").Should().Be(IndentOf("Dough"));
    });

    [Fact]
    public void Without_its_costs_a_scaled_recipe_prints_only_the_amounts() => StaThread.Run(() =>
    {
        var texts = AllTexts(new ProductPrintLayout(SamplePrintouts.Scaled(), includeRecipe: true, includeCosting: false, A4));

        texts.Should().ContainInConsecutiveOrder("Sellable Units", "20", "Pieces", "160");
        texts.Should().ContainInConsecutiveOrder("Item", "Amount", "Dough   2 batches · 200 pcs (40 extra)", "2,000 g", "Flour", "1,800 g");
        texts.Should().NotContain(t => t.Contains("Cost", StringComparison.Ordinal), "no Cost column and no Total Cost figure");
        texts.Should().NotContain([Figure(90m, "N2"), Figure(108m, "N2"), Figure(708m, "N2")]);
    });

    [Fact]
    public void A_scaled_recipe_too_long_for_a_page_runs_on_under_its_heading_and_header_row_again() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(SamplePrintouts.Scaled(extraLines: 80), includeRecipe: true, includeCosting: true, A4);

        layout.PageCount.Should().BeGreaterThan(1);
        var body = Body(layout.BuildPage(2));
        Texts(body[0]).Single().Should().Be("Scaling (continued)");
        Texts(body[1]).Should().Equal("Item", "Amount", "Cost");
        EveryPageShouldFitAboveItsFooter(layout, A4);
    });

    [Fact]
    public void A_scaled_section_that_doesnt_fit_whats_left_of_a_page_starts_the_next_with_the_row_naming_it() => StaThread.Run(() =>
    {
        // Each fits a page, but not both on one: the Filling goes over whole rather than starting at the foot of the first.
        var layout = new ProductPrintLayout(
            SamplePrintouts.ScaledSections(("Dough", 25), ("Filling", 25)), includeRecipe: true, includeCosting: true, A4);

        layout.PageCount.Should().Be(2);
        AllTexts(layout, 1).Should().NotContain(t => t.StartsWith("Filling", StringComparison.Ordinal));
        var body = Body(layout.BuildPage(2));
        Texts(body[0]).Single().Should().Be("Scaling (continued)");
        Texts(body[1]).Should().Equal("Item", "Amount", "Cost");
        Texts(body[2]).First().Should().Be("Filling   1 batch · 20 pcs");
        EveryPageShouldFitAboveItsFooter(layout, A4);
    });

    [Fact]
    public void A_product_sold_by_the_piece_has_no_pieces_figure_and_one_with_nothing_to_scale_says_so() => StaThread.Run(() =>
    {
        var texts = AllTexts(new ProductPrintLayout(
            SamplePrintouts.Scaled() with { Scaling = new PrintedScaling(120, null, [], 0m) }, includeRecipe: true, includeCosting: true, A4));

        texts.Should().ContainInConsecutiveOrder("Sellable Units", "120", "Total Cost", Figure(0m, "N2"));
        texts.Should().NotContain("Pieces").And.NotContain("Item");
        texts.Should().Contain("Nothing to scale yet.");
    });

    [Fact]
    public void A_price_simulator_report_lists_the_changes_tried_then_each_products_base_cost_now_and_with_them() => StaThread.Run(() =>
    {
        var layout = new ProductPrintLayout(SamplePrintouts.SimulationSet("Cheese Roll", "Ube Roll"), includeRecipe: true, includeCosting: true, A4);
        var sample = SamplePrintouts.Simulation();
        var now = sample.Simulation!.Now.BaseCost;
        var simulated = sample.Costs.BaseCost;

        layout.DocumentName.Should().Be("Price Simulator — Recipe and Costing");
        var cover = AllTexts(layout, 1);
        cover[0].Should().Be(PriceSimulatorReport.Title);
        cover.Should().ContainInConsecutiveOrder("Price Changes", "Raw Material", "Per", "Current Price", "Change %", "New Price");
        cover.Should().ContainInConsecutiveOrder("Bread Flour", "sack", Figure(1250m, "N2"), "+10.00%", Figure(1375m, "N2"));
        cover.Should().ContainInConsecutiveOrder("Products", "Product", "Base Cost", "Simulated Base Cost", "Change %", "Page");
        cover.Should().ContainInConsecutiveOrder(
            "Cheese Roll", Figure(now, "N2"), Figure(simulated, "N2"), $"+{Figure(CostingRules.PercentOf(simulated - now, now), "N2")}%", "2");
        EveryPageShouldFitAboveItsFooter(layout, A4);
    });

    [Fact]
    public void A_price_simulator_products_pages_have_the_changes_applied_the_moved_line_with_its_cost_now_and_the_costing_now_and_with_them() => StaThread.Run(() =>
    {
        var sample = SamplePrintouts.Simulation();
        var texts = AllTexts(new ProductPrintLayout(SamplePrintouts.SimulationSet("Ube Roll"), includeRecipe: true, includeCosting: true, A4));

        texts.Should().Contain(t => t.EndsWith(" · Per piece · Simulated prices", StringComparison.Ordinal));
        texts.Should().ContainInConsecutiveOrder("Price Changes Applied", "Raw Material", "Per", "Current Price", "Change %", "New Price", "Bread Flour");
        texts.Should().ContainInConsecutiveOrder("Ingredient", "Grams", "Baker's %", "Cost/g", "Cost", "Cost Now", "Base");
        texts.Should().ContainInConsecutiveOrder("Ingredient 1", Figure(100m, "N1"), Figure(10m, "N1"), Figure(0.05m, "N4"), Figure(5m, "N2"), Figure(4m, "N2"));
        texts.Should().ContainInConsecutiveOrder("Ingredient 2", Figure(100m, "N1"), Figure(10m, "N1"), Figure(0.05m, "N4"), Figure(5m, "N2"), string.Empty);
        texts.Should().ContainInConsecutiveOrder("Total", Figure(200m, "N1"), Figure(20m, "N1"), string.Empty, Figure(10m, "N2"), Figure(9m, "N2"), string.Empty);
        texts.Should().ContainInConsecutiveOrder("Material", "Qty", "Unit", "Unit Cost", "Cost", "Cost Now", "Gift Box", Figure(1m, "N1"), "pc", Figure(30m, "N4"), Figure(30m, "N4"), string.Empty);

        texts.Should().ContainInConsecutiveOrder("Base Cost", Figure(sample.Costs.BaseCost, "N2"), $"Now {Figure(sample.Simulation!.Now.BaseCost, "N2")}");
        var keep = PriceChangeRules.PriceToKeepMarkup(sample.Costs.CurrentPrice, sample.Simulation.Now.BaseCost, sample.Costs.BaseCost)!.Value;
        texts.Should().ContainInConsecutiveOrder(
            "Current Price", Figure(sample.Costs.CurrentPrice!.Value, "N2"), $"{ProductPrintLayout.KeepMarkupNote} {Figure(keep, "N2")}");
        texts.Should().ContainInConsecutiveOrder("Component", "Now", "Simulated", "Change", "% of Price");
        texts.Should().ContainInConsecutiveOrder("Dough", Figure(2m, "N2"), Figure(2.5m, "N2"), "+0.50");
        texts.Should().ContainInConsecutiveOrder("Packaging", Figure(30m, "N2"), Figure(30m, "N2"), "—");
    });

    [Fact]
    public void A_price_simulator_product_no_change_reaches_says_so_and_without_the_costing_the_report_prints_no_costs_or_changes() => StaThread.Run(() =>
    {
        var untouched = SamplePrintouts.SimulationSet("Ube Roll") with
        {
            Products = [SamplePrintouts.Simulation() with { Simulation = new PrintedSimulation(SamplePrintouts.Simulation().Costs, []) }],
        };
        var untouchedTexts = AllTexts(new ProductPrintLayout(untouched, includeRecipe: true, includeCosting: true, A4));
        untouchedTexts.Should().Contain("No price change reaches this product: its figures are today's.");
        untouchedTexts.Should().NotContain(t => t.StartsWith(ProductPrintLayout.KeepMarkupNote, StringComparison.Ordinal), "its current price already keeps its markup");

        var kitchen = AllTexts(new ProductPrintLayout(SamplePrintouts.SimulationSet("Ube Roll"), includeRecipe: true, includeCosting: false, A4));
        kitchen.Should().ContainInConsecutiveOrder("Product", "Folder", "Page");
        kitchen.Should().NotContain(["Price Changes", "Price Changes Applied", "Cost Now", "Simulated Base Cost", "Bread Flour"]);
        kitchen.Should().NotContain(t => t.Contains("Simulated prices", StringComparison.Ordinal));
    });

    /// <summary>Lays every page out at <paramref name="size"/>, as the pane does, and checks its blocks end above its footer.</summary>
    private static void EveryPageShouldFitAboveItsFooter(ProductPrintLayout layout, Size size)
    {
        for (var number = 1; number <= layout.PageCount; number++)
        {
            var page = LaidOut(layout, number, size);
            Body(page).Sum(block => block.DesiredSize.Height)
                .Should().BeLessThanOrEqualTo(size.Height - Footer(page).DesiredSize.Height, $"page {number} of {layout.PageCount} is laid out as it was measured");
        }
    }

    /// <summary>Page <paramref name="number"/>, measured and arranged at <paramref name="size"/>.</summary>
    private static Grid LaidOut(ProductPrintLayout layout, int number, Size size)
    {
        var page = (Grid)layout.BuildPage(number);
        page.Measure(size);
        page.Arrange(new Rect(size));
        return page;
    }

    /// <summary>The blocks a page's body holds, in order.</summary>
    private static List<FrameworkElement> Body(FrameworkElement page) =>
        [.. ((StackPanel)((Grid)page).Children[0]).Children.Cast<FrameworkElement>()];

    /// <summary>A page's footer, under its body.</summary>
    private static FrameworkElement Footer(Grid page) => (FrameworkElement)page.Children[1];

    /// <summary>
    /// The text of every TextBlock in <paramref name="element"/>, in order: all of its runs, which its Text doesn't give
    /// for one built from runs.
    /// </summary>
    private static List<string> Texts(DependencyObject element) =>
        element is TextBlock text
            ? [new TextRange(text.ContentStart, text.ContentEnd).Text]
            : [.. LogicalTreeHelper.GetChildren(element).OfType<DependencyObject>().SelectMany(Texts)];

    private static string Figure(decimal value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

    /// <summary>The texts of page <paramref name="number"/>'s body.</summary>
    private static List<string> AllTexts(ProductPrintLayout layout, int number) =>
        [.. Body(layout.BuildPage(number)).SelectMany(Texts)];

    private static List<string> AllTexts(ProductPrintLayout layout) =>
        [.. Enumerable.Range(1, layout.PageCount).SelectMany(number => Texts(layout.BuildPage(number)))];
}
