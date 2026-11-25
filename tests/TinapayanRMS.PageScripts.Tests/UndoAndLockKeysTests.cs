using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// The undo keys (registerUndoKeys, UndoKeys): Ctrl+Z, Ctrl+Y and Ctrl+Shift+Z leave the field first, so what's half typed
/// lands before the screen steps back, and put the cursor back once the step is drawn. And the locked window's keys
/// (lockKeys, SignInScreen): no key reaches the screens under the sign-in panel, while its own boxes type and edit.
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class UndoAndLockKeysTests(EdgeBrowser edge)
{
    private const string Markup =
        """
        <span id="vim-mode" hidden></span>
        <input id="name" type="text">
        <div id="panel"><input id="password" type="text"></div>
        """;

    [Test]
    [Arguments("Control+z", "[false]")]
    [Arguments("Control+y", "[true]")]
    [Arguments("Control+Shift+z", "[true]")]
    public async Task The_undo_keys_step_the_screen_back_or_forward(string key, string asked)
    {
        await using var page = await OpenUndoAsync();
        await page.Page.ClickAsync("#name");

        await page.Keyboard.PressAsync(key);

        await page.Page.WaitForFunctionAsync("() => window.dotNetCalls.length > 0");
        (await page.CallsOfAsync("Undo")).Should().Equal(asked);
    }

    [Test]
    [Arguments("Control+Shift+y")]
    [Arguments("Control+Alt+z")]
    [Arguments("z")]
    public async Task Other_keys_arent_undo(string key)
    {
        await using var page = await OpenUndoAsync();
        await page.Page.ClickAsync("#name");

        await page.Keyboard.PressAsync(key);
        await page.Page.WaitForTimeoutAsync(100);

        (await page.CallsOfAsync("Undo")).Should().BeEmpty();
    }

    [Test]
    public async Task Whats_half_typed_lands_before_the_step_and_the_cursor_comes_back_after()
    {
        await using var page = await OpenUndoAsync();
        await page.EvaluateAsync(
            """
            () => {
                window.order = [];
                document.getElementById("name").addEventListener("change", () => window.order.push("change"));
                window.dotNetAnswers.Undo = () => { window.order.push("Undo:" + (document.activeElement?.id ?? "")); return new Promise(done => setTimeout(done, 50)); };
            }
            """);
        await page.Page.ClickAsync("#name");
        await page.Keyboard.TypeAsync("Ube");

        await page.Keyboard.PressAsync("Control+z");

        await page.Page.WaitForFunctionAsync("() => document.activeElement?.id === 'name'");
        (await page.EvaluateAsync<string[]>("window.order")).Should().Equal("change", "Undo:");
    }

    [Test]
    public async Task Locked_no_key_reaches_the_screens_and_the_panels_box_still_types_and_edits()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EvaluateAsync(
            """
            () => {
                window.heard = [];
                document.addEventListener("keydown", event => window.heard.push(event.key));
                window.find = tinapayan.registerFindKey(window.dotNet);
                window.lock = tinapayan.lockKeys(document.getElementById("panel"));
            }
            """);

        await page.Page.ClickAsync("#name");
        await page.Keyboard.TypeAsync("abc");
        await page.Page.ClickAsync("#password");
        await page.Keyboard.TypeAsync("pandesal");
        await page.Keyboard.PressAsync("Control+a");
        await page.Keyboard.TypeAsync("6am");
        await page.Keyboard.PressAsync("Control+k");

        // A key outside the panel is stopped before any handler of the page's (the screens under it are inert, so the
        // cursor isn't there in the window: it's put there here only to press a key outside the panel).
        (await page.ValueOfAsync("#password")).Should().Be("6am", "Ctrl+A still selects in the panel's box");
        (await page.CallsAsync()).Should().NotContain("OpenFromKeyAsync", "Find doesn't open over the panel");
        (await page.EvaluateAsync<string>("window.heard.join('')")).Should().Be("pandesal6am",
            "the document's key watches hear only the panel's own typing, which Enter signs in with: no key outside it, and no Ctrl key");
    }

    [Test]
    public async Task Unlocked_the_keys_go_through_again()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EvaluateAsync("tinapayan.lockKeys(document.getElementById('panel')).dispose()");

        await page.Page.ClickAsync("#name");
        await page.Keyboard.TypeAsync("abc");

        (await page.ValueOfAsync("#name")).Should().Be("abc");
    }

    private async Task<ScriptPage> OpenUndoAsync()
    {
        var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EvaluateAsync("window.undoKeys = tinapayan.registerUndoKeys(window.dotNet)");
        return page;
    }
}
