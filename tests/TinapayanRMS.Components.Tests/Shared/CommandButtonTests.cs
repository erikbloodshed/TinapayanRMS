using AwesomeAssertions;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>CommandButton: a button bound to a screen state's command, enabled while the command can run.</summary>
public class CommandButtonTests
{
    [Test]
    public async Task A_click_runs_the_command()
    {
        await using var context = AppServices.NewContext();
        var runs = 0;
        var command = ScreenCommand.Of(() => { runs++; });
        var button = context.Render<CommandButton>(parameters => parameters.Add(b => b.Command, command).Add(b => b.Label, "Save"));

        button.Find("button").Click();

        // The click's handler runs on the renderer's dispatcher, so its result is waited for, not assumed.
        await button.WaitForAssertionAsync(() => runs.Should().Be(1));
        button.Find(".command-label").TextContent.Should().Be("Save");
    }

    [Test]
    public async Task The_button_is_disabled_while_the_command_cant_run()
    {
        await using var context = AppServices.NewContext();
        var canRun = false;
        var runs = 0;
        var command = ScreenCommand.Of(() => { runs++; }, () => canRun);
        var button = context.Render<CommandButton>(parameters => parameters.Add(b => b.Command, command).Add(b => b.Label, "Save"));

        button.Find("button").HasAttribute("disabled").Should().BeTrue();

        // The screen redraws its buttons when a command's CanExecute changes (ScreenBase); here the test does.
        canRun = true;
        button.Render();

        button.Find("button").HasAttribute("disabled").Should().BeFalse();
        button.Find("button").Click();
        await button.WaitForAssertionAsync(() => runs.Should().Be(1));
    }

    [Test]
    public async Task A_screen_command_runs_on_a_click_and_disables_the_button_while_it_cant_run()
    {
        await using var context = AppServices.NewContext();
        var picked = false;
        var runs = 0;
        var command = ScreenCommand.Of(() => runs++, () => picked);
        var button = context.Render<CommandButton>(parameters => parameters.Add(b => b.Command, command).Add(b => b.Label, "Delete"));

        button.Find("button").HasAttribute("disabled").Should().BeTrue();

        // Asked as the button is drawn: the screen draws it again as anything on it changes (ScreenBase).
        picked = true;
        button.Render();
        button.Find("button").HasAttribute("disabled").Should().BeFalse();
        button.Find("button").Click();

        runs.Should().Be(1, "it runs as it's clicked, with no scheduler between");
    }

    [Test]
    public async Task A_button_with_only_an_icon_is_an_icon_button()
    {
        await using var context = AppServices.NewContext();
        var command = ScreenCommand.Of(() => { });

        var button = context.Render<CommandButton>(parameters => parameters.Add(b => b.Command, command).Add(b => b.Icon, AppIcon.Add).Add(b => b.Flat, true));

        button.Find("button").ClassList.Should().Contain(["icon-only", "e-flat"]);
        button.Find("button .glyph").ClassList.Should().Contain(["e-icons", "e-plus"], "Syncfusion's icon draws it");
        button.FindAll(".command-label").Should().BeEmpty();
    }

    [Test]
    public async Task Its_tooltip_is_the_pages_own_and_names_an_icon_button()
    {
        await using var context = AppServices.NewContext();
        var command = ScreenCommand.Of(() => { });

        var iconOnly = context.Render<CommandButton>(parameters => parameters.Add(b => b.Command, command).Add(b => b.Icon, AppIcon.Add).Add(b => b.Tooltip, "Add a line"));
        var labelled = context.Render<CommandButton>(parameters => parameters.Add(b => b.Command, command).Add(b => b.Label, "Save").Add(b => b.Tooltip, "Saves it"));

        iconOnly.Find("button").GetAttribute("data-title").Should().Be("Add a line", "the page's one SfTooltip shows it (MainLayout)");
        iconOnly.Find("button").HasAttribute("title").Should().BeFalse("a title would bring up the browser's own tooltip as well");
        iconOnly.Find("button").GetAttribute("aria-label").Should().Be("Add a line", "with no label, its tooltip is its name");
        labelled.Find("button").HasAttribute("aria-label").Should().BeFalse("its label names it");
    }
}
