using AwesomeAssertions;
using TinapayanRMS.PageScripts.Tests.Fixtures;

namespace TinapayanRMS.PageScripts.Tests;

/// <summary>
/// The Vim keys (vim.js), turned on with the app's own key table (VimKeymap) as the VimKeys component turns them on:
/// NORMAL, where letters move and act, and INSERT, where they type, shown in the title bar's #vim-mode. Keys are pressed
/// by Playwright's keyboard, trusted as a person's are: vim.js leaves untrusted ones alone.
/// </summary>
[ClassDataSource<EdgeBrowser>(Shared = SharedType.PerTestSession)]
public sealed class VimKeysTests(EdgeBrowser edge)
{
    private const string Markup =
        """
        <span id="vim-mode" hidden></span>
        <div class="app-page">
            <input id="first" type="text" value="Pandesal">
            <input id="second" type="text" value="Ensaymada">
            <button id="save">Save</button>
        </div>
        """;

    [Test]
    public async Task Turned_on_with_nothing_typed_into_the_keys_are_in_NORMAL()
    {
        await using var page = await OpenAsync();

        (await ModeAsync(page)).Should().Be("NORMAL");
        (await page.EvaluateAsync<bool>("document.getElementById('vim-mode').hidden")).Should().BeFalse();
    }

    [Test]
    public async Task A_click_into_a_field_types_and_Esc_stops_typing_keeping_what_was_typed()
    {
        await using var page = await OpenAsync();

        await page.Page.ClickAsync("#first");
        (await ModeAsync(page)).Should().Be("INSERT");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync(" Big");
        await page.Keyboard.PressAsync("Escape");

        (await ModeAsync(page)).Should().Be("NORMAL");
        (await page.ValueOfAsync("#first")).Should().Be("Pandesal Big");
        (await page.FocusedIdAsync()).Should().Be("first", "stopping typing leaves the cursor where it was");
    }

    [Test]
    public async Task In_NORMAL_letters_and_Backspace_dont_change_a_field()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#first");

        await page.Keyboard.TypeAsync("xyz");
        await page.Keyboard.PressAsync("Backspace");

        (await page.ValueOfAsync("#first")).Should().Be("Pandesal");
        (await ModeAsync(page)).Should().Be("NORMAL");
    }

    [Test]
    public async Task j_and_k_move_between_fields_as_Tab_does_without_typing()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#first");

        await page.Keyboard.PressAsync("j");
        (await page.FocusedIdAsync()).Should().Be("second");
        (await ModeAsync(page)).Should().Be("NORMAL", "a move the keys made doesn't start typing");

        await page.Keyboard.PressAsync("j");
        (await page.FocusedIdAsync()).Should().Be("save");

        await page.Keyboard.PressAsync("k");
        await page.Keyboard.PressAsync("k");
        (await page.FocusedIdAsync()).Should().Be("first");
    }

    [Test]
    public async Task cc_types_over_the_field()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#first");

        await page.Keyboard.TypeAsync("cc");
        (await ModeAsync(page)).Should().Be("INSERT");
        await page.Keyboard.TypeAsync("Monay");

        (await page.ValueOfAsync("#first")).Should().Be("Monay");
    }

    [Test]
    [Arguments("A", " Big", "Pandesal Big")]
    [Arguments("I", "Big ", "Big Pandesal")]
    public async Task A_and_I_type_at_the_end_and_at_the_start(string key, string typed, string shown)
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#first");

        await page.Keyboard.PressAsync($"Shift+{key}");
        await page.Keyboard.TypeAsync(typed);

        (await page.ValueOfAsync("#first")).Should().Be(shown);
    }

    [Test]
    public async Task A_key_half_typed_is_dropped_by_one_that_means_nothing_after_it()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#first");

        // c then x is nothing: the c is dropped, so the next c alone starts no change.
        await page.Keyboard.TypeAsync("cxc");

        (await ModeAsync(page)).Should().Be("NORMAL");
        (await page.ValueOfAsync("#first")).Should().Be("Pandesal");
    }

    [Test]
    public async Task f_labels_what_can_be_clicked_and_a_label_typed_clicks_it()
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync(
            """
            () => {
                window.dotNetAnswers.HintLabelsFor = count => ["a", "s", "d", "f"].slice(0, count);
                window.saved = 0;
                document.getElementById("save").addEventListener("click", () => window.saved++);
            }
            """);
        await page.Page.FocusAsync("#save");

        await page.Keyboard.PressAsync("f");
        await page.Page.WaitForSelectorAsync(".vim-hint");
        (await page.EvaluateAsync<string[]>("[...document.querySelectorAll('.vim-hint')].map(tag => tag.textContent)")).Should().Equal("a", "s", "d");
        await page.Keyboard.PressAsync("d");

        (await page.EvaluateAsync<int>("window.saved")).Should().Be(1);
        (await page.EvaluateAsync<int>("document.querySelectorAll('.vim-hint').length")).Should().Be(0, "the labels go once one is picked");
        (await page.CallsAsync()).Should().Equal("HintLabelsFor");
    }

    [Test]
    public async Task A_label_on_a_field_types_into_it()
    {
        await using var page = await OpenAsync();
        await page.EvaluateAsync("""window.dotNetAnswers.HintLabelsFor = count => ["a", "s", "d"].slice(0, count)""");
        await page.Page.FocusAsync("#save");

        await page.Keyboard.PressAsync("f");
        await page.Page.WaitForSelectorAsync(".vim-hint");
        await page.Keyboard.PressAsync("s");

        (await page.FocusedIdAsync()).Should().Be("second");
        (await ModeAsync(page)).Should().Be("INSERT");
    }

    /// <summary>Find (a dialog marked closes-on-first-esc) closes on the first Esc while typing, as with the keys off (2026-11-17).</summary>
    [Test]
    public async Task Esc_while_typing_in_Find_goes_on_to_close_it_where_another_dialog_takes_two()
    {
        await using var page = await ScriptPage.OpenAsync(edge,
            """
            <span id="vim-mode" hidden></span>
            <div id="find" class="e-dialog e-popup-open closes-on-first-esc"><input id="find-box" type="text"></div>
            """);
        await page.EnableVimKeysAsync();
        await CountEscapesAsync(page, "#find");
        await page.Page.ClickAsync("#find-box");

        await page.Keyboard.PressAsync("Escape");

        (await page.EvaluateAsync<int>("window.escapes")).Should().Be(1, "Find hears the first Esc and closes");
        (await ModeAsync(page)).Should().Be("NORMAL");
    }

    [Test]
    public async Task Esc_while_typing_in_another_dialog_only_stops_the_typing()
    {
        await using var page = await ScriptPage.OpenAsync(edge,
            """
            <span id="vim-mode" hidden></span>
            <div id="prompt" class="e-dialog e-popup-open"><input id="prompt-box" type="text"></div>
            """);
        await page.EnableVimKeysAsync();
        await CountEscapesAsync(page, "#prompt");
        await page.Page.ClickAsync("#prompt-box");

        await page.Keyboard.PressAsync("Escape");
        (await page.EvaluateAsync<int>("window.escapes")).Should().Be(0);
        (await ModeAsync(page)).Should().Be("NORMAL");

        await page.Keyboard.PressAsync("Escape");
        (await page.EvaluateAsync<int>("window.escapes")).Should().Be(1, "the second Esc is the dialog's");
    }

    /// <summary>
    /// A list's name box (TreeNameBox) takes the cursor typing: the first Esc stops the typing, and only the second reaches
    /// the box, which gives the new row up (by design, 2026-11-14; Find is the one dialog that closes on the first).
    /// </summary>
    [Test]
    public async Task Esc_in_a_lists_name_box_stops_typing_first_and_reaches_the_box_on_the_second()
    {
        await using var page = await ScriptPage.OpenAsync(edge,
            """
            <span id="vim-mode" hidden></span>
            <div class="folder-tree" tabindex="0"><div class="tree-row"><input id="name-box" type="text" value="New Folder"></div></div>
            """);
        await page.EnableVimKeysAsync();
        await CountEscapesAsync(page, "#name-box");
        await page.Page.ClickAsync("#name-box");
        (await ModeAsync(page)).Should().Be("INSERT");

        await page.Keyboard.PressAsync("Escape");
        (await page.EvaluateAsync<int>("window.escapes")).Should().Be(0);
        (await ModeAsync(page)).Should().Be("NORMAL");

        await page.Keyboard.PressAsync("Escape");
        (await page.EvaluateAsync<int>("window.escapes")).Should().Be(1);
    }

    [Test]
    public async Task Turned_off_the_keys_type_again()
    {
        await using var page = await OpenAsync();
        await StopTypingInAsync(page, "#first");

        await page.EvaluateAsync("tinapayanVim.disable()");
        await page.Keyboard.PressAsync("End");
        await page.Keyboard.TypeAsync("j");

        (await page.ValueOfAsync("#first")).Should().Be("Pandesalj");
        (await page.EvaluateAsync<bool>("document.getElementById('vim-mode').hidden")).Should().BeTrue();
    }

    private async Task<ScriptPage> OpenAsync()
    {
        var page = await ScriptPage.OpenAsync(edge, Markup);
        await page.EnableVimKeysAsync();
        return page;
    }

    /// <summary>The cursor put in a field by a click, which starts typing, then Esc, which stops it there.</summary>
    private static async Task StopTypingInAsync(ScriptPage page, string selector)
    {
        await page.Page.ClickAsync(selector);
        await page.Keyboard.PressAsync("Escape");
        (await ModeAsync(page)).Should().Be("NORMAL");
    }

    private static Task<string> ModeAsync(ScriptPage page) => page.EvaluateAsync<string>("document.getElementById('vim-mode').textContent");

    /// <summary>Counts each Esc that reaches the dialog's own handler, in window.escapes.</summary>
    private static Task CountEscapesAsync(ScriptPage page, string dialog) =>
        page.EvaluateAsync(
            """
            selector => {
                window.escapes = 0;
                document.querySelector(selector).addEventListener("keydown", event => { if (event.key === "Escape") window.escapes++; });
            }
            """,
            dialog);
}
