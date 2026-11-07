using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Layout;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Layout;

/// <summary>
/// The window's title bar, which the page draws in place of Windows' own, as the WPF window's ChromelessWindow draws it:
/// what it shows, and what it asks of the window (IAppWindow). Dragging it is Windows' own, so only where the bar and its
/// buttons are is checked here; the window's caption itself shows only in the app.
/// </summary>
public class TitleBarTests
{
    [Test]
    public async Task It_has_the_heading_then_the_windows_buttons_on_the_right()
    {
        await using var context = AppServices.NewContext().RenderingAsTheWebView();

        var bar = context.Render<TitleBar>();

        bar.Find(".app-heading").TextContent.Should().Be("TINAPAYAN FESTIVAL BAKESHOPPE — RECIPE & COSTING");
        bar.FindAll("button").Select(b => b.GetAttribute("aria-label")).Should().Equal(
            "Navigation menu", "Find", "Back", "Forward", "Minimize", "Maximize", "Close");
    }

    [Test]
    public async Task The_menu_button_opens_the_menu()
    {
        await using var context = AppServices.NewContext().RenderingAsTheWebView();
        var clicks = 0;

        var bar = context.Render<TitleBar>(parameters => parameters.Add(b => b.OnMenu, () => clicks++));
        await bar.Find("button[aria-label='Navigation menu']").ClickAsync(new());

        clicks.Should().Be(1);
    }

    [Test]
    public async Task Minimize_and_Close_are_the_windows()
    {
        await using var context = AppServices.NewContext().RenderingAsTheWebView();
        var window = context.Services.GetRequiredService<FakeAppWindow>();
        var bar = context.Render<TitleBar>();

        await bar.Find("button[aria-label='Minimize']").ClickAsync(new());
        await bar.Find("button[aria-label='Close']").ClickAsync(new());

        window.Minimized.Should().Be(1);
        window.Closed.Should().Be(1, "the window closes as Windows' own Close button closes it, asking about unsaved changes first");
    }

    [Test]
    public async Task Maximize_turns_into_Restore_Down_while_the_window_is_maximized()
    {
        await using var context = AppServices.NewContext().RenderingAsTheWebView();
        var window = context.Services.GetRequiredService<FakeAppWindow>();
        var bar = context.Render<TitleBar>();

        await bar.Find("button[aria-label='Maximize']").ClickAsync(new());

        window.IsMaximized.Should().BeTrue();
        bar.WaitForAssertion(() => bar.FindAll(".caption-button")[1].GetAttribute("aria-label").Should().Be("Restore Down"));

        await bar.Find("button[aria-label='Restore Down']").ClickAsync(new());

        window.IsMaximized.Should().BeFalse();
        bar.WaitForAssertion(() => bar.FindAll(".caption-button")[1].GetAttribute("aria-label").Should().Be("Maximize"));
    }

    [Test]
    public async Task Where_the_bar_and_its_buttons_are_goes_to_the_window()
    {
        await using var context = AppServices.NewContext().RenderingAsTheWebView();
        var window = context.Services.GetRequiredService<FakeAppWindow>();
        var bar = context.Render<TitleBar>();
        DeviceRect[] buttons = [new(0, 0, 50, 40), new(2070, 0, 50, 40)];

        // As app.js sends it, in device pixels.
        bar.Instance.SetTitleBar(new DeviceRect(0, 0, 2120, 40), buttons);

        window.TitleBar.Should().NotBeNull();
        window.TitleBar!.Value.TitleBar.Should().Be(new DeviceRect(0, 0, 2120, 40));
        window.TitleBar.Value.Buttons.Should().Equal(buttons);
    }

    [Test]
    public async Task Back_and_Forward_follow_the_web_views_history()
    {
        await using var context = AppServices.NewContext().RenderingAsTheWebView();
        var bar = context.Render<TitleBar>();

        await bar.InvokeAsync(() => bar.Instance.HistoryChanged(canGoBack: true, canGoForward: false));

        bar.WaitForAssertion(() =>
        {
            bar.Find("button[aria-label='Back']").HasAttribute("disabled").Should().BeFalse();
            bar.Find("button[aria-label='Forward']").HasAttribute("disabled").Should().BeTrue();
        });
    }
}
