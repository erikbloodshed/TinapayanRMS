using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using AwesomeAssertions;
using TinapayanRMS.Domain.Enums;
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
