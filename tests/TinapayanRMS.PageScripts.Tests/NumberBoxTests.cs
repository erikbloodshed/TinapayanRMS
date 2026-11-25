using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// A number box (NumberField, a <c>.number-field</c> around its input) takes only what makes a number, refused by app.js
/// before the character lands: digits, commas, one point unless it's a count (data-whole), and a minus first only where
/// the number may go below zero (data-signed). A paste or a drop goes in at the caret only when it's one number, a
/// currency sign and spaces aside, that the box takes there.
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class NumberBoxTests(EdgeBrowser edge)
{
    private const string Markup =
        """
        <span class="number-field"><input id="price" type="text"></span>
        <span class="number-field"><input id="count" type="text" data-whole="true"></span>
        <span class="number-field"><input id="change" type="text" data-signed="true"></span>
        <span class="number-field"><input id="locked" type="text" readonly value="12"></span>
        <input id="name" type="text">
        """;

    [Test]
    [Arguments("12a.5.0", "12.50")]
    [Arguments("1,250", "1,250")]
    [Arguments("₱3", "3")]
    [Arguments("-5", "5")]
    [Arguments("2 x 3", "23")]
    public async Task A_price_takes_digits_commas_and_one_point_and_nothing_else(string typed, string shown)
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await page.Page.ClickAsync("#price");
        await page.Keyboard.TypeAsync(typed);

        (await page.ValueOfAsync("#price")).Should().Be(shown);
    }

    [Test]
    public async Task A_count_takes_no_point()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await page.Page.ClickAsync("#count");
        await page.Keyboard.TypeAsync("3.5");

        (await page.ValueOfAsync("#count")).Should().Be("35");
    }

    [Test]
    [Arguments("-5", "-5")]
    [Arguments("5-", "5")]
    [Arguments("--2.5", "-2.5")]
    public async Task A_box_that_may_go_below_zero_takes_a_minus_first_only(string typed, string shown)
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await page.Page.ClickAsync("#change");
        await page.Keyboard.TypeAsync(typed);

        (await page.ValueOfAsync("#change")).Should().Be(shown);
    }

    [Test]
    public async Task A_text_box_takes_anything()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await page.Page.ClickAsync("#name");
        await page.Keyboard.TypeAsync("Ube 1/2 kg");

        (await page.ValueOfAsync("#name")).Should().Be("Ube 1/2 kg");
    }

    [Test]
    [Arguments("₱1,250.00", "1,250.00")]
    [Arguments(" 3.5 ", "3.5")]
    [Arguments("$12", "12")]
    [Arguments("1/2", "")]
    [Arguments("3 x 250 g", "")]
    [Arguments("12.50 (3 pcs)", "")]
    [Arguments("-₱5", "")]
    [Arguments("Pandesal", "")]
    public async Task A_paste_goes_in_when_its_one_number_the_box_takes(string pasted, string shown)
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await PasteAsync(page, "#price", pasted);

        (await page.ValueOfAsync("#price")).Should().Be(shown);
    }

    [Test]
    public async Task A_negative_paste_goes_into_a_box_that_may_go_below_zero()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await PasteAsync(page, "#change", "-₱5");

        (await page.ValueOfAsync("#change")).Should().Be("-5");
    }

    [Test]
    public async Task A_paste_goes_in_at_the_caret_and_tells_the_box()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.Page.FillAsync("#price", "1.25");
        await page.EvaluateAsync(
            """
            () => {
                window.inputs = 0;
                document.getElementById("price").addEventListener("input", () => window.inputs++);
            }
            """);

        await PasteAsync(page, "#price", "0", caret: 1);

        (await page.ValueOfAsync("#price")).Should().Be("10.25");
        (await page.EvaluateAsync<int>("window.inputs")).Should().Be(1, "the server hears of it as of a key");
    }

    /// <summary>A drop of "5.5" into "1.25" made "1.255.5" (2026-11-08): what it would make at the caret is checked first.</summary>
    [Test]
    [Arguments("5.5", "1.25")]
    [Arguments("5", "1.255")]
    public async Task A_drop_goes_in_at_the_caret_only_when_it_leaves_a_number(string dropped, string shown)
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.Page.FillAsync("#price", "1.25");

        await page.EvaluateAsync(
            """
            text => {
                const box = document.getElementById("price");
                box.focus();
                box.setSelectionRange(box.value.length, box.value.length);
                const data = new DataTransfer();
                data.setData("text/plain", text);
                box.dispatchEvent(new DragEvent("drop", { dataTransfer: data, bubbles: true, cancelable: true }));
            }
            """,
            dropped);

        (await page.ValueOfAsync("#price")).Should().Be(shown);
    }

    [Test]
    public async Task A_paste_into_a_read_only_number_box_or_a_text_box_is_the_browsers_own()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        (await PasteAsync(page, "#locked", "5")).Should().BeFalse();
        (await PasteAsync(page, "#name", "1/2")).Should().BeFalse();
        (await PasteAsync(page, "#price", "1/2")).Should().BeTrue("a number box's paste is the script's, refused here");
    }

    /// <summary>
    /// A paste as the browser raises it, with the text in its clipboard data: a headless browser has no clipboard to paste
    /// from. The box takes the cursor first (which selects a number box's text), then the caret goes to
    /// <paramref name="caret"/>, or the end. True when the page's script took the paste over (prevented the browser's own).
    /// </summary>
    private static Task<bool> PasteAsync(ScriptPage page, string selector, string text, int? caret = null) =>
        page.EvaluateAsync<bool>(
            """
            ([selector, text, caret]) => {
                const box = document.querySelector(selector);
                box.focus();
                const at = caret ?? box.value.length;
                box.setSelectionRange(at, at);
                const data = new DataTransfer();
                data.setData("text/plain", text);
                return !box.dispatchEvent(new ClipboardEvent("paste", { clipboardData: data, bubbles: true, cancelable: true }));
            }
            """,
            new object?[] { selector, text, caret });
}
