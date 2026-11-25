using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// The page-wide keys app.js registers for the components: Find's Ctrl+K from anywhere, a field included (registerFindKey),
/// and the web view's own "Leave site?" while a form holds unsaved changes (setLeaveWarning).
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class ShortcutTests(EdgeBrowser edge)
{
    private const string Markup = """<input id="name" type="text"><button id="save">Save</button>""";

    [Test]
    public async Task Ctrl_K_opens_Find_from_a_field_and_types_nothing()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EvaluateAsync("window.find = tinapayan.registerFindKey(window.dotNet)");
        await page.Page.ClickAsync("#name");

        await page.Keyboard.PressAsync("Control+k");

        (await page.CallsAsync()).Should().Equal("OpenFromKeyAsync");
        (await page.ValueOfAsync("#name")).Should().BeEmpty();
    }

    [Test]
    [Arguments("k")]
    [Arguments("Control+Shift+k")]
    [Arguments("Control+Alt+k")]
    public async Task Other_keys_dont_open_Find(string key)
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EvaluateAsync("window.find = tinapayan.registerFindKey(window.dotNet)");
        await page.Page.ClickAsync("#name");

        await page.Keyboard.PressAsync(key);

        (await page.CallsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task Ctrl_K_does_nothing_once_its_let_go()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EvaluateAsync("tinapayan.registerFindKey(window.dotNet).dispose()");
        await page.Page.ClickAsync("#save");

        await page.Keyboard.PressAsync("Control+k");

        (await page.CallsAsync()).Should().BeEmpty();
    }

    [Test]
    public async Task The_leave_warning_is_asked_for_while_its_on_and_not_after()
    {
        await using var page = await ScriptPage.OpenAsync(edge, Markup);
        const string LeaveIsQuestioned =
            "() => { const event = new Event('beforeunload', { cancelable: true }); window.dispatchEvent(event); return event.defaultPrevented; }";

        await page.EvaluateAsync("tinapayan.setLeaveWarning(true)");
        await page.EvaluateAsync("tinapayan.setLeaveWarning(true)");
        (await page.EvaluateAsync<bool>(LeaveIsQuestioned)).Should().BeTrue();

        await page.EvaluateAsync("tinapayan.setLeaveWarning(false)");
        (await page.EvaluateAsync<bool>(LeaveIsQuestioned)).Should().BeFalse("turning it on twice registered it once, so once off it's gone");
    }
}
