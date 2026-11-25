using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// Dragging by the pointer, as app.js does it, since the Windows web view never hands the browser's own drop back to the
/// page: a table's lines into order by their row headers (watchLineDrag, LineGrid's MoveLine), and a list's rows onto a
/// folder (watchTreeDrag, FolderTree's Start, Over, Drop and Cancel). A press moved under 4 pixels drags nothing; Escape
/// or a release beside the table or list moves nothing.
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class DragTests(EdgeBrowser edge)
{
    private const string Lines =
        """
        <style>body { margin: 20px; } td { height: 30px; padding: 0 8px; } .row-header { width: 20px; user-select: none; }</style>
        <table id="lines"><tbody>
            <tr><td class="row-header" data-drag id="h0">=</td><td>Flour</td></tr>
            <tr><td class="row-header" data-drag id="h1">=</td><td>Sugar</td></tr>
            <tr><td class="row-header" data-drag id="h2">=</td><td>Butter</td></tr>
            <tr><td class="row-header" data-drag id="h3">=</td><td>Eggs</td></tr>
        </tbody></table>
        """;

    private const string Tree =
        """
        <style>body { margin: 20px; } .folder-tree { width: 300px; user-select: none; } .tree-row { height: 28px; }</style>
        <div class="folder-tree" id="tree" tabindex="0">
            <div class="tree-row" id="tree-folder-breads" aria-expanded="true"><span class="tree-title">Breads</span></div>
            <div class="tree-row" id="tree-product-1" data-drag><span class="tree-title">Pandesal</span></div>
            <div class="tree-row" id="tree-product-2" data-drag><span class="tree-title">Monay</span></div>
            <div class="tree-row" id="tree-folder-cakes" aria-expanded="false"><span class="tree-title">Cakes</span></div>
        </div>
        """;

    [Test]
    [Arguments("#h0", "#h2", "[0,2]")]
    [Arguments("#h3", "#h1", "[3,1]")]
    public async Task A_line_dragged_by_its_row_header_is_moved_where_its_let_go(string from, string onto, string moved)
    {
        await using var page = await OpenLinesAsync();

        await page.DragAsync(from, await page.MiddleOfAsync(onto));

        (await page.CallsOfAsync("MoveLine")).Should().Equal(moved);
        (await page.EvaluateAsync<int>("document.querySelectorAll('.drop-above, .drop-below, .dragging-line').length")).Should().Be(0);
    }

    [Test]
    public async Task While_a_line_is_dragged_the_line_under_the_pointer_shows_where_it_goes()
    {
        await using var page = await OpenLinesAsync();

        await page.DragAsync("#h0", await page.MiddleOfAsync("#h2"), release: false);

        (await page.EvaluateAsync<string[]>("[...document.querySelectorAll('#lines tr')].map(row => row.className)"))
            .Should().Equal("dragging-line", "", "drop-below", "");
        await page.Page.Mouse.UpAsync();
    }

    [Test]
    public async Task A_press_that_hardly_moves_drags_no_line_though_it_crosses_into_the_next()
    {
        await using var page = await OpenLinesAsync();
        var header = await page.Page.Locator("#h0").BoundingBoxAsync();
        var x = header!.X + header.Width / 2;
        var bottom = await page.EvaluateAsync<float>("document.getElementById('h0').parentElement.getBoundingClientRect().bottom");

        // Pressed a pixel above the line's foot and let go 3 pixels lower, over the next line: under the 4 a drag needs.
        await page.Page.Mouse.MoveAsync(x, bottom - 1);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(x, bottom + 2);
        await page.Page.Mouse.UpAsync();

        (await page.CallsOfAsync("MoveLine")).Should().BeEmpty();
    }

    [Test]
    public async Task A_line_let_go_beside_the_table_or_after_Escape_stays_where_it_was()
    {
        await using var page = await OpenLinesAsync();
        var target = await page.MiddleOfAsync("#h2");

        await page.DragAsync("#h0", (target.X + 400, target.Y));
        await page.DragAsync("#h0", target, release: false);
        await page.Keyboard.PressAsync("Escape");
        await page.Page.Mouse.UpAsync();

        (await page.CallsOfAsync("MoveLine")).Should().BeEmpty();
    }

    [Test]
    public async Task A_row_dragged_onto_a_folder_is_dropped_there_and_the_copy_following_the_pointer_says_where()
    {
        await using var page = await OpenTreeAsync();

        await page.DragAsync("#tree-product-1", await page.MiddleOfAsync("#tree-folder-cakes"), release: false);
        await page.Page.WaitForSelectorAsync(".tree-drag-ghost-caption:not([hidden])");
        (await page.EvaluateAsync<string>("document.querySelector('.tree-drag-ghost-caption').textContent")).Should().Be("→ Move to Cakes");
        (await page.EvaluateAsync<string>("document.querySelector('.tree-drag-ghost-title').textContent")).Should().Be("Pandesal");
        await page.Page.Mouse.UpAsync();

        (await page.CallsOfAsync("Start")).Should().Equal("[\"tree-product-1\"]");
        (await page.CallsOfAsync("Drop")).Should().Equal("[\"tree-folder-cakes\"]");
        (await page.EvaluateAsync<int>("document.querySelectorAll('.tree-drag-ghost').length")).Should().Be(0);
    }

    [Test]
    public async Task Several_rows_dragged_together_show_how_many()
    {
        await using var page = await OpenTreeAsync();
        await page.EvaluateAsync("window.dotNetAnswers.Start = () => 3");

        await page.DragAsync("#tree-product-1", await page.MiddleOfAsync("#tree-folder-cakes"), release: false);

        await page.Page.WaitForSelectorAsync(".tree-drag-ghost-count:not([hidden])");
        (await page.EvaluateAsync<string>("document.querySelector('.tree-drag-ghost-count').textContent")).Should().Be("3");
        await page.Page.Mouse.UpAsync();
    }

    [Test]
    public async Task A_row_that_cant_be_dragged_has_no_copy_following_the_pointer()
    {
        await using var page = await OpenTreeAsync();
        await page.EvaluateAsync("window.dotNetAnswers.Start = () => 0");

        await page.DragAsync("#tree-product-1", await page.MiddleOfAsync("#tree-folder-cakes"), release: false);

        await page.Page.WaitForFunctionAsync("() => !document.querySelector('.tree-drag-ghost')");
        await page.Page.Mouse.UpAsync();
    }

    [Test]
    public async Task A_row_let_go_beside_the_list_or_after_Escape_is_cancelled()
    {
        await using var page = await OpenTreeAsync();
        var folder = await page.MiddleOfAsync("#tree-folder-cakes");

        await page.DragAsync("#tree-product-1", (folder.X + 500, folder.Y));
        await page.DragAsync("#tree-product-2", folder, release: false);
        await page.Keyboard.PressAsync("Escape");
        await page.Page.Mouse.UpAsync();

        (await page.CallsOfAsync("Cancel")).Should().HaveCount(2);
        (await page.CallsOfAsync("Drop")).Should().BeEmpty();
    }

    [Test]
    public async Task The_click_a_drop_makes_picks_nothing_but_a_click_still_does()
    {
        await using var page = await OpenTreeAsync();
        await page.EvaluateAsync("() => { window.clicks = 0; document.addEventListener('click', () => window.clicks++); }");

        await page.DragAsync("#tree-product-1", await page.MiddleOfAsync("#tree-product-2"));
        (await page.EvaluateAsync<int>("window.clicks")).Should().Be(0, "a drop doesn't pick or open the row too");

        await page.Page.WaitForTimeoutAsync(450);
        await page.Page.ClickAsync("#tree-product-2");
        (await page.EvaluateAsync<int>("window.clicks")).Should().Be(1);
    }

    /// <summary>
    /// A press let go beside the list before it moved left its drag behind, and the pointer coming back over the list
    /// with no button down dragged the row, the next click dropping it: a move saved at once.
    /// </summary>
    [Test]
    public async Task A_press_let_go_beside_the_list_drags_nothing_when_the_pointer_comes_back()
    {
        await using var page = await OpenTreeAsync();
        var row = await page.MiddleOfAsync("#tree-product-1");
        var folder = await page.MiddleOfAsync("#tree-folder-cakes");

        await page.Page.Mouse.MoveAsync(row.X, row.Y);
        await page.Page.Mouse.DownAsync();
        await page.Page.Mouse.MoveAsync(row.X + 600, row.Y);
        await page.Page.Mouse.UpAsync();

        // Back over the list, onto a folder, with no button down; then a click there.
        await page.Page.Mouse.MoveAsync(folder.X, folder.Y, new() { Steps = 8 });
        await page.Page.Mouse.ClickAsync(folder.X, folder.Y);

        (await page.CallsAsync()).Should().NotContain(["Start", "Drop"]);
    }

    private async Task<ScriptPage> OpenLinesAsync()
    {
        var page = await ScriptPage.OpenAsync(edge, Lines);
        await page.EvaluateAsync("window.lineDrag = tinapayan.watchLineDrag(document.getElementById('lines'), window.dotNet)");
        return page;
    }

    private async Task<ScriptPage> OpenTreeAsync()
    {
        var page = await ScriptPage.OpenAsync(edge, Tree);
        await page.EvaluateAsync(
            """
            () => {
                window.dotNetAnswers.Start = () => 1;
                window.dotNetAnswers.Over = id => id === "tree-folder-cakes" || id === "tree-folder-breads";
                window.treeDrag = tinapayan.watchTreeDrag(document.getElementById("tree"), window.dotNet);
            }
            """);
        return page;
    }
}
