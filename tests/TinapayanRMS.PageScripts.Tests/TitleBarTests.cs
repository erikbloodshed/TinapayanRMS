using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// The title bar the page draws (TitleBar.razor): the window is told where the bar and its buttons are, in device pixels,
/// so Windows takes the rest as the caption (watchTitleBar's SetTitleBar), again only when something in it moved; and
/// Back and Forward follow the web view's history (HistoryChanged).
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class TitleBarTests(EdgeBrowser edge)
{
    private const string Markup =
        """
        <style>
            body { margin: 0; }
            #bar { display: flex; height: 32px; width: 900px; }
            #bar button { width: 46px; height: 32px; border: 0; }
            #bar .gap { flex: 1; }
        </style>
        <div id="bar">
            <span class="title">TinapayanRMS</span><span class="gap"></span>
            <button id="find">F</button><button id="back">B</button><button id="close">X</button>
        </div>
        """;

    [Test]
    public async Task The_window_is_told_where_the_bar_and_each_button_are()
    {
        await using var page = await OpenAsync();

        await page.Page.WaitForFunctionAsync("() => window.dotNetCalls.some(call => call.name === 'SetTitleBar')");

        var told = await page.EvaluateAsync<int[][]>(
            "() => { const [bar, buttons] = window.dotNetCalls.find(call => call.name === 'SetTitleBar').args; return [[bar.x, bar.y, bar.width, bar.height], ...buttons.map(b => [b.x, b.width])]; }");
        told[0].Should().Equal(0, 0, 900, 32);
        told.Skip(1).Select(button => button[1]).Should().Equal(46, 46, 46);
        told.Skip(1).Select(button => button[0]).Should().BeInAscendingOrder().And.OnlyContain(x => x > 700);
        (await page.CallsAsync()).Should().Contain("HistoryChanged");
    }

    [Test]
    public async Task Its_told_again_when_a_button_goes_and_not_when_nothing_moved()
    {
        await using var page = await OpenAsync();
        await page.Page.WaitForFunctionAsync("() => window.dotNetCalls.some(call => call.name === 'SetTitleBar')");

        await page.EvaluateAsync("window.dispatchEvent(new Event('resize'))");
        await page.Page.WaitForTimeoutAsync(200);
        (await page.CallsOfAsync("SetTitleBar")).Should().HaveCount(1, "nothing moved");

        await page.EvaluateAsync("document.getElementById('find').hidden = true");
        await page.Page.WaitForFunctionAsync("() => window.dotNetCalls.filter(call => call.name === 'SetTitleBar').length === 2");
        (await page.EvaluateAsync<int>("window.dotNetCalls.filter(call => call.name === 'SetTitleBar')[1].args[1].length")).Should().Be(2);
    }

    private async Task<ScriptPage> OpenAsync()
    {
        var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EvaluateAsync("window.titleBar = tinapayan.watchTitleBar(document.getElementById('bar'), window.dotNet)");
        return page;
    }
}
