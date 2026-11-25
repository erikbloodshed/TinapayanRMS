using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// The Vim keys in a list (vim.js): a table typed straight into (LineGrid's <c>.line-grid</c>), where j, k, h and l move
/// between its cells, o types a new line and dd removes one; a grid of rows to pick from (PickGrid, an <c>.e-grid</c>),
/// where j and k pick the next row; and the lists of records (FolderTree's <c>.folder-tree</c>), where each key presses
/// the arrow or the key it stands for, for the tree to answer.
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class VimListKeysTests(EdgeBrowser edge)
{
    private const string LineGrid =
        """
        <span id="vim-mode" hidden></span>
        <div class="app-page">
            <input id="before" type="text">
            <table class="line-grid"><tbody>
                <tr><td><input id="r0c0" value="Flour"></td><td><input id="r0c1" value="500"></td></tr>
                <tr><td><input id="r1c0" value="Sugar"></td><td><input id="r1c1" value="100"></td></tr>
                <tr><td><input id="r2c0" value="Butter"></td><td><input id="r2c1" value="50"></td></tr>
                <tr class="add-row"><td><input id="new0"></td><td><input id="new1"></td></tr>
            </tbody></table>
            <button class="remove-line" id="remove">Remove</button>
            <input id="after" type="text">
        </div>
        """;

    [Test]
    [Arguments("j", "r2c1")]
    [Arguments("k", "r0c1")]
    [Arguments("h", "r1c0")]
    [Arguments("gg", "r0c1")]
    [Arguments("G", "new1")]
    [Arguments("0", "r1c0")]
    [Arguments("J", "remove")]
    [Arguments("K", "before")]
    public async Task In_a_table_the_keys_move_between_its_cells_keeping_the_column(string keys, string landsOn)
    {
        await using var page = await OpenAsync(LineGrid);
        await StopTypingInAsync(page, "#r1c1");

        await page.Keyboard.TypeAsync(keys);

        (await page.FocusedIdAsync()).Should().Be(landsOn);
        (await ModeAsync(page)).Should().Be("NORMAL");
    }

    [Test]
    public async Task o_types_a_new_line_in_the_empty_row_at_the_bottom()
    {
        await using var page = await OpenAsync(LineGrid);
        await StopTypingInAsync(page, "#r0c1");

        await page.Keyboard.TypeAsync("o");
        await page.Keyboard.TypeAsync("Eggs");

        (await page.ValueOfAsync("#new0")).Should().Be("Eggs");
        (await ModeAsync(page)).Should().Be("INSERT");
    }

    [Test]
    public async Task dd_removes_the_line_with_the_cursor_by_the_tables_Remove()
    {
        await using var page = await OpenAsync(LineGrid);
        await page.EvaluateAsync("() => { window.removed = 0; document.getElementById('remove').addEventListener('click', () => window.removed++); }");
        await StopTypingInAsync(page, "#r1c0");

        await page.Keyboard.TypeAsync("dd");

        (await page.EvaluateAsync<int>("window.removed")).Should().Be(1);
    }

    [Test]
    public async Task dd_in_the_empty_row_removes_nothing()
    {
        await using var page = await OpenAsync(LineGrid);
        await page.EvaluateAsync("() => { window.removed = 0; document.getElementById('remove').addEventListener('click', () => window.removed++); }");
        await StopTypingInAsync(page, "#new0");

        await page.Keyboard.TypeAsync("dd");

        (await page.EvaluateAsync<int>("window.removed")).Should().Be(0);
    }

    [Test]
    [Arguments("j", 2)]
    [Arguments("k", 0)]
    [Arguments("G", 3)]
    [Arguments("gg", 0)]
    public async Task In_a_grid_to_pick_from_the_keys_pick_another_row(string keys, int picked)
    {
        await using var page = await OpenAsync(
            """
            <span id="vim-mode" hidden></span>
            <div class="e-grid"><div class="e-gridcontent"><table><tbody>
                <tr class="e-row"><td class="e-rowcell" tabindex="0">Pandesal</td></tr>
                <tr class="e-row" aria-selected="true"><td class="e-rowcell" tabindex="0" id="picked">Monay</td></tr>
                <tr class="e-row"><td class="e-rowcell" tabindex="0">Ensaymada</td></tr>
                <tr class="e-row"><td class="e-rowcell" tabindex="0">Ube Roll</td></tr>
            </tbody></table></div></div>
            """);
        // As the grid picks a row when a cell is clicked.
        await page.EvaluateAsync(
            """
            () => document.querySelectorAll(".e-rowcell").forEach(cell => cell.addEventListener("click", () => {
                document.querySelectorAll(".e-row").forEach(row => row.removeAttribute("aria-selected"));
                cell.parentElement.setAttribute("aria-selected", "true");
            }))
            """);
        await page.Page.FocusAsync("#picked");

        await page.Keyboard.TypeAsync(keys);

        (await page.EvaluateAsync<int>("[...document.querySelectorAll('.e-row')].findIndex(row => row.getAttribute('aria-selected') === 'true')")).Should().Be(picked);
    }

    [Test]
    public async Task In_a_list_of_records_each_key_presses_the_key_it_stands_for()
    {
        await using var page = await OpenAsync(
            """
            <span id="vim-mode" hidden></span>
            <div class="folder-tree" id="tree" tabindex="0"><div class="tree-row">Breads</div></div>
            """);
        await page.EvaluateAsync("() => { window.pressed = []; document.getElementById('tree').addEventListener('keydown', event => { if (!event.isTrusted) window.pressed.push(event.key); }); }");
        await page.Page.FocusAsync("#tree");

        await page.Keyboard.TypeAsync("jklggGodd");
        await page.Keyboard.PressAsync("r");

        (await page.EvaluateAsync<string[]>("window.pressed")).Should().Equal("ArrowDown", "ArrowUp", "ArrowRight", "Home", "End", "Enter", "Delete", "F2");
    }

    private async Task<ScriptPage> OpenAsync(string markup)
    {
        var page = await ScriptPage.OpenAsync(edge, markup);
        await page.EnableVimKeysAsync();
        return page;
    }

    private static async Task StopTypingInAsync(ScriptPage page, string selector)
    {
        await page.Page.ClickAsync(selector);
        await page.Keyboard.PressAsync("Escape");
        (await ModeAsync(page)).Should().Be("NORMAL");
    }

    private static Task<string> ModeAsync(ScriptPage page) => page.EvaluateAsync<string>("document.getElementById('vim-mode').textContent");
}
