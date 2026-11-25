using System.Text;
using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// A printout's pages, which app.js works out before each printing (beforeprint) from the heights its parts' rows have on
/// the screen: a table that goes on to the next page is broken between rows, with "Continued on page n" under the last
/// row of the page, and the part's name, its section and the table's column headings again at the top of the next. A set's
/// contents gets the page each product starts on. Each part here is a <c>.print-sheet</c> of rows 30 px high in a page
/// 100 mm high, as PrintDocument.razor draws one with Settings → Printing's paper.
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class PrintPagesTests(EdgeBrowser edge)
{
    private const string Style =
        """
        <style>
            body { margin: 0; font: 14px sans-serif; }
            .print-sheet { padding: 0; }
            h1, h2, p { margin: 0; height: 30px; line-height: 30px; }
            table { border-collapse: collapse; }
            th, td { padding: 0; height: 30px; }
        </style>
        """;

    [Test]
    public async Task A_table_that_goes_on_is_broken_between_rows_with_its_name_section_and_headings_again()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Style + Document(Product("Asado Roll 60g", "Filling", 20)));

        await PrintAsync(page);

        var running = await page.EvaluateAsync<string[]>("[...document.querySelectorAll('.print-running-row td')].map(cell => cell.textContent + '|' + cell.dataset.page)");
        running.Should().NotBeEmpty("20 rows of 30 px don't fit a page 100 mm high");
        running[0].Should().Be("Asado Roll 60g · Filling (continued)|Page 2");
        (await page.EvaluateAsync<string[]>("[...document.querySelectorAll('.print-continued')].map(row => row.textContent)"))
            .Should().StartWith("Continued on page 2");
        (await page.EvaluateAsync<int>("document.querySelectorAll('.print-repeat-head').length")).Should().Be(running.Length, "each page a table goes on to has its headings again");
        (await page.EvaluateAsync<int>("document.querySelectorAll('[data-page-break]').length")).Should().Be(running.Length);
        (await page.EvaluateAsync<bool>("document.querySelector('.print-document').classList.contains('print-paged')")).Should().BeTrue();
    }

    [Test]
    public async Task No_page_is_fuller_than_the_paper()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Style + Document(Product("Asado Roll 60g", "Filling", 40)));

        await PrintAsync(page);

        // From each page's break (or the part's top) to the next, what's laid out now, running rows added, fits the page.
        var heights = await page.EvaluateAsync<double[]>(
            """
            () => {
                const sheet = document.querySelector(".print-sheet");
                const top = sheet.getBoundingClientRect().top;
                const breaks = [...sheet.querySelectorAll("[data-page-break]")].map(element => element.getBoundingClientRect().top - top);
                const edges = [0, ...breaks, sheet.getBoundingClientRect().bottom - top];
                return edges.slice(1).map((edge, index) => edge - edges[index]);
            }
            """);
        // The paper's 100 mm less the 3 mm app.js leaves for what the printed layout may differ from the screen's by
        // (printPage.slack): the sheet has no padding, so its top is where app.js measures the first page from.
        var limit = (100 - 3) * 96 / 25.4;
        heights.Should().HaveCountGreaterThan(2).And.OnlyContain(height => height <= limit);
    }

    [Test]
    public async Task Working_the_pages_out_again_puts_nothing_in_twice()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Style + Document(Product("Asado Roll 60g", "Filling", 20)));

        await PrintAsync(page);
        var once = await page.EvaluateAsync<int>("document.querySelectorAll('.print-added').length");
        await PrintAsync(page);

        (await page.EvaluateAsync<int>("document.querySelectorAll('.print-added').length")).Should().Be(once);
    }

    [Test]
    public async Task A_part_that_fits_one_page_gets_no_break()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Style + Document(Product("Pandesal", "Dough", 3)));

        await PrintAsync(page);

        (await page.EvaluateAsync<int>("document.querySelectorAll('.print-added, [data-page-break]').length")).Should().Be(0);
    }

    [Test]
    public async Task A_sets_contents_gets_the_page_each_product_starts_on()
    {
        var contents =
            """
            <section class="print-sheet"><div class="print-title"><h1>Contents</h1></div>
            <table><tbody><tr><td>Asado Roll 60g</td><td class="print-contents-page"></td></tr><tr><td>Pandesal</td><td class="print-contents-page"></td></tr></tbody></table>
            </section>
            """;
        await using var page = await ScriptPage.OpenAsync(edge, Style + Document(contents + Product("Asado Roll 60g", "Filling", 20) + Product("Pandesal", "Dough", 3)));

        await PrintAsync(page);

        var asadoPages = 1 + await page.EvaluateAsync<int>("document.querySelectorAll('.print-product')[0].querySelectorAll('[data-page-break]').length");
        (await page.EvaluateAsync<string[]>("[...document.querySelectorAll('.print-contents-page')].map(cell => cell.textContent)"))
            .Should().Equal("2", (2 + asadoPages).ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static Task PrintAsync(ScriptPage page) => page.EvaluateAsync("window.dispatchEvent(new Event('beforeprint'))");

    private static string Document(string sheets) => $"""<div class="print-document" data-page-height="100">{sheets}</div>""";

    /// <summary>A product's part: its name, a section heading, and a table of <paramref name="rows"/> ingredient rows.</summary>
    private static string Product(string name, string section, int rows)
    {
        var body = new StringBuilder();
        for (var row = 1; row <= rows; row++)
        {
            body.Append(System.Globalization.CultureInfo.InvariantCulture, $"<tr><td>Ingredient {row}</td><td>{row * 10} g</td></tr>");
        }

        return $"""
            <section class="print-sheet print-product">
                <div class="print-title"><h1>{name}</h1></div>
                <h2>{section}</h2>
                <table><thead><tr><th>Ingredient</th><th>Quantity</th></tr></thead><tbody>{body}</tbody></table>
            </section>
            """;
    }
}
