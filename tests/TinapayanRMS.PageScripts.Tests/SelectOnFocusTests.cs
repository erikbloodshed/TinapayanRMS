using AwesomeAssertions;
using Microsoft.Playwright;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// A number box's or a picker's text is selected as it takes the cursor, so what's typed replaces it (app.js). After a
/// click that brought the cursor in, it's selected again once the click is over; but a box the cursor came into by Tab is
/// clicked in to put the caret somewhere, and selecting it all then had the next key replace the whole number (2026-11-08).
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class SelectOnFocusTests(EdgeBrowser edge)
{
    private const string Markup =
        """
        <style>input { font: 16px monospace; width: 200px; }</style>
        <input id="before" type="text" value="Monay">
        <span class="number-field"><input id="price" type="text" value="1250.75"></span>
        <span class="deferred-pick"><input id="pick" type="text" value="First Class Flour"></span>
        <input id="combo" class="e-combobox" type="text" value="Sugar">
        <span class="number-field"><input id="locked" type="text" readonly value="12"></span>
        <input id="name" type="text" value="Pandesal">
        """;

    [Test]
    [Arguments("#price")]
    [Arguments("#pick")]
    [Arguments("#combo")]
    public async Task A_click_into_a_number_box_or_a_picker_selects_its_text(string selector)
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await page.Page.ClickAsync(selector, new PageClickOptions { Position = new Position { X = 8, Y = 8 } });

        (await SelectionAsync(page, selector)).Should().Equal(0, (await page.ValueOfAsync(selector)).Length);
    }

    [Test]
    public async Task A_click_into_a_text_box_puts_the_caret_where_it_lands()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await page.Page.ClickAsync("#name", new PageClickOptions { Position = new Position { X = 8, Y = 8 } });

        var selection = await SelectionAsync(page, "#name");
        selection[0].Should().Be(selection[1], "nothing is selected");
    }

    [Test]
    public async Task A_click_in_a_number_box_the_cursor_came_into_by_Tab_puts_the_caret_where_it_lands()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.Page.FocusAsync("#before");
        await page.Keyboard.PressAsync("Tab");
        (await page.FocusedIdAsync()).Should().Be("price");
        (await SelectionAsync(page, "#price")).Should().Equal([0, 7], "Tab selects it too");
        await page.Keyboard.TypeAsync("12345");

        // Inside what was typed, by the mouse: as the bug went, Tab in, type, click between two digits.
        await page.Page.ClickAsync("#price", new PageClickOptions { Position = new Position { X = 30, Y = 8 } });

        var selection = await SelectionAsync(page, "#price");
        selection[0].Should().Be(selection[1]).And.BeGreaterThan(0);
        await page.Keyboard.TypeAsync("9");
        (await page.ValueOfAsync("#price")).Length.Should().Be(6, "the 9 went in at the caret, replacing nothing");
    }

    [Test]
    public async Task A_read_only_number_box_isnt_selected()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);

        await page.Page.ClickAsync("#locked", new PageClickOptions { Position = new Position { X = 8, Y = 8 } });

        var selection = await SelectionAsync(page, "#locked");
        selection[0].Should().Be(selection[1]);
    }

    private static Task<int[]> SelectionAsync(ScriptPage page, string selector) =>
        page.EvaluateAsync<int[]>("selector => { const box = document.querySelector(selector); return [box.selectionStart, box.selectionEnd]; }", selector);
}
