using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// The Vim keys' commands (vim.js): what's typed after : goes to the screen (VimKeys' RunCommandLine), which answers with
/// a message, an address or the cheat sheet; ? shows the keys for where the cursor is; Ctrl+R = while typing works out a
/// sum (EvaluateSum) and types its answer; u and Ctrl+R press the undo keys; gt and gT step through the screens; / goes to
/// the screen's search box.
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class VimCommandTests(EdgeBrowser edge)
{
    private const string Markup =
        """
        <span id="vim-mode" hidden></span>
        <nav>
            <a class="activity-item active" href="raw-materials" id="screen-raw">Raw Materials</a>
            <a class="activity-item" href="products" id="screen-products">Products</a>
            <a class="activity-item" href="reports" id="screen-reports">Reports</a>
            <a class="activity-item" href="settings" id="screen-settings">Settings</a>
        </nav>
        <div class="app-page">
            <span class="list-search"><input id="search" type="text"></span>
            <input id="name" type="text" value="Pandesal">
            <span class="number-field"><input id="quantity" type="text" data-decimals="2"></span>
        </div>
        """;

    [Test]
    public async Task A_command_goes_to_the_screen_and_its_message_shows()
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync("""window.dotNetAnswers.RunCommandLine = line => ({ message: "Saved " + line })""");
        await StopTypingInAsync(page, "#name");

        await page.Keyboard.TypeAsync(":");
        (await page.EvaluateAsync<bool>("document.activeElement === document.querySelector('.vim-bar input')")).Should().BeTrue();
        await page.Keyboard.TypeAsync("w");
        await page.Keyboard.PressAsync("Enter");

        await page.Page.WaitForSelectorAsync(".vim-flash");
        (await page.EvaluateAsync<string>("document.querySelector('.vim-flash').textContent")).Should().Be("Saved w");
        (await page.CallsOfAsync("RunCommandLine")).Should().Equal("[\"w\"]");
        (await page.FocusedIdAsync()).Should().Be("name", "the cursor goes back where it was");
        (await ModeAsync(page)).Should().Be("NORMAL");
    }

    [Test]
    public async Task Esc_closes_the_command_line_running_nothing()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#name");

        await page.Keyboard.TypeAsync(":wq");
        await page.Keyboard.PressAsync("Escape");

        (await page.EvaluateAsync<int>("document.querySelectorAll('.vim-bar').length")).Should().Be(0);
        (await page.CallsOfAsync("RunCommandLine")).Should().BeEmpty();
        (await page.FocusedIdAsync()).Should().Be("name");
    }

    [Test]
    public async Task A_command_that_names_an_address_goes_there()
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync(
            """
            () => {
                // The page's address as the window's is: the app's addresses are taken from it.
                document.head.prepend(Object.assign(document.createElement("base"), { href: "https://0.0.0.1/" }));
                window.wentTo = [];
                window.Blazor = { navigateTo: url => window.wentTo.push(new URL(url).pathname) };
                window.dotNetAnswers.RunCommandLine = () => ({ url: "products/42" });
            }
            """);
        await StopTypingInAsync(page, "#name");

        await page.Keyboard.TypeAsync(":e asado");
        await page.Keyboard.PressAsync("Enter");

        await page.Page.WaitForFunctionAsync("() => window.wentTo.length > 0");
        (await page.EvaluateAsync<string[]>("window.wentTo")).Should().Equal("/products/42");
    }

    [Test]
    public async Task Question_mark_shows_the_keys_for_a_field_and_the_commands_and_Esc_closes_them()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#name");

        await page.Keyboard.TypeAsync("?");

        var sheet = await page.EvaluateAsync<string>("document.querySelector('.vim-sheet')?.innerText ?? ''");
        sheet.Should().Contain("Here: in a field").And.Contain("Next field, as Tab").And.Contain(":w").And.NotContain("Delete the line");
        await page.Keyboard.PressAsync("Escape");
        (await page.EvaluateAsync<int>("document.querySelectorAll('.vim-sheet').length")).Should().Be(0);
    }

    [Test]
    public async Task Ctrl_R_then_equals_while_typing_works_out_a_sum_and_types_its_answer_over_whats_selected()
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync("""window.dotNetAnswers.EvaluateSum = (text, decimals) => ({ answer: "25.00" })""");
        await page.Page.ClickAsync("#quantity");

        await page.Keyboard.PressAsync("Control+r");
        await page.Keyboard.PressAsync("=");
        await page.Keyboard.TypeAsync("5*(2+3)");
        await page.Keyboard.PressAsync("Enter");

        await page.Page.WaitForFunctionAsync("() => document.getElementById('quantity').value === '25.00'");
        (await page.CallsOfAsync("EvaluateSum")).Should().Equal(["[\"5*(2+3)\",2]"], "the box's decimals go with it");
        (await page.FocusedIdAsync()).Should().Be("quantity");
        (await ModeAsync(page)).Should().Be("INSERT", "typing goes on where it was");
    }

    [Test]
    public async Task A_sum_that_cant_be_worked_out_says_why_and_stays_open()
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync("""window.dotNetAnswers.EvaluateSum = () => ({ error: "A bracket isn't closed." })""");
        await page.Page.ClickAsync("#quantity");

        await page.Keyboard.PressAsync("Control+r");
        await page.Keyboard.PressAsync("=");
        await page.Keyboard.TypeAsync("5*(2");
        await page.Keyboard.PressAsync("Enter");

        await page.Page.WaitForFunctionAsync("() => document.querySelector('.vim-sum-error')?.textContent === \"A bracket isn't closed.\"");
        (await page.ValueOfAsync("#quantity")).Should().BeEmpty();
        await page.Keyboard.PressAsync("Escape");
        (await page.FocusedIdAsync()).Should().Be("quantity");
    }

    [Test]
    public async Task u_undoes_and_Ctrl_R_redoes_through_the_screens_undo_keys()
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync("window.undoKeys = tinapayan.registerUndoKeys(window.dotNet)");
        await StopTypingInAsync(page, "#name");

        await page.Keyboard.PressAsync("u");
        await page.Keyboard.PressAsync("Control+r");

        await page.Page.WaitForFunctionAsync("() => window.dotNetCalls.filter(call => call.name === 'Undo').length === 2");
        (await page.CallsOfAsync("Undo")).Should().Equal("[false]", "[true]");
    }

    [Test]
    [Arguments("gt", "screen-products")]
    [Arguments("gT", "screen-reports")]
    public async Task gt_and_gT_go_to_the_next_screen_and_the_one_before_passing_Settings_over(string keys, string opened)
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync("() => { window.opened = []; document.querySelectorAll('.activity-item').forEach(item => item.addEventListener('click', event => { event.preventDefault(); window.opened.push(item.id); })); }");
        await StopTypingInAsync(page, "#name");

        await page.Keyboard.TypeAsync(keys);

        (await page.EvaluateAsync<string[]>("window.opened")).Should().Equal(opened);
    }

    [Test]
    public async Task Slash_goes_to_the_screens_search_box()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#name");

        await page.Keyboard.TypeAsync("/");

        (await page.FocusedIdAsync()).Should().Be("search");
    }

    private async Task<ScriptPage> OpenAsync()
    {
        var page = await ScriptPage.OpenAsync(edge, Markup);
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
