using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Components.Layout;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Components.Tests.Layout;

/// <summary>
/// The locked window (AppLock): the sign-in panel, which sets the first password or takes the password, and the layout,
/// which draws no screen until the first sign-in and keeps the screen it drew, out of reach, under the panel after that.
/// </summary>
public class SignInTests
{
    private static readonly TimeSpan Hashing = TimeSpan.FromSeconds(10);

    private static BunitContext NewContext(InMemoryAccessSettings settings)
    {
        var context = AppServices.NewContext();
        context.Services.AddSingleton<IAccessSettingsRepository>(settings);
        return context.RenderingAsTheWebView();
    }

    private static async Task TypeAsync(IRenderedComponent<SignInScreen> panel, string id, string text, bool enter = false)
    {
        var box = panel.Find($"#{id}");
        await box.InputAsync(new ChangeEventArgs { Value = text });
        if (enter)
        {
            await panel.Find($"#{id}").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        }
    }

    [Test]
    public async Task With_no_password_the_panel_asks_for_one_twice_and_setting_it_unlocks()
    {
        var settings = new InMemoryAccessSettings();
        await using var context = NewContext(settings);
        var appLock = context.Services.GetRequiredService<AppLock>();

        var panel = context.Render<SignInScreen>();
        panel.WaitForAssertion(() => panel.FindAll("input[type=password]").Should().HaveCount(2));

        await TypeAsync(panel, "sign-in-password", "pandesal 6am");
        await TypeAsync(panel, "sign-in-confirmation", "pandesal 6am", enter: true);

        panel.WaitForAssertion(() => appLock.IsLocked.Should().BeFalse(), Hashing);
        (await settings.VerifyAsync("pandesal 6am", CancellationToken.None)).Should().BeTrue();
    }

    [Test]
    public async Task A_wrong_password_says_so_in_the_panel_with_its_box_emptied_and_the_right_one_unlocks()
    {
        var settings = new InMemoryAccessSettings();
        await settings.CreatePasswordAsync("pandesal 6am", CancellationToken.None);
        await using var context = NewContext(settings);
        var appLock = context.Services.GetRequiredService<AppLock>();

        var panel = context.Render<SignInScreen>();
        panel.WaitForAssertion(() => panel.FindAll("input[type=password]").Should().ContainSingle());
        panel.Find(".caption").TextContent.Should().Be("Type the password to open the app.");

        await TypeAsync(panel, "sign-in-password", "pandesal 7am", enter: true);
        panel.WaitForAssertion(() => panel.Find(".sign-in-error").TextContent.Should().Be("That isn't the password."), Hashing);
        panel.Find("#sign-in-password").GetAttribute("value").Should().BeEmpty("the box is drawn afresh after a try");
        appLock.IsLocked.Should().BeTrue();

        await TypeAsync(panel, "sign-in-password", "pandesal 6am", enter: true);
        panel.WaitForAssertion(() => appLock.IsLocked.Should().BeFalse(), Hashing);
    }

    [Test]
    public async Task The_change_Enter_sends_while_the_password_is_checked_doesnt_come_back_in_the_fresh_box()
    {
        var settings = new InMemoryAccessSettings();
        await settings.CreatePasswordAsync("pandesal 6am", CancellationToken.None);
        await using var context = NewContext(settings);
        var signIn = context.Services.GetRequiredService<SignInViewModel>();
        var panel = context.Render<SignInScreen>();
        panel.WaitForAssertion(() => panel.FindAll("input[type=password]").Should().ContainSingle());
        await panel.Find("#sign-in-password").InputAsync(new ChangeEventArgs { Value = "pandesal 7am" });

        // As the web view sends them: the key, then the box's text as a change, which lands while the check runs.
        settings.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var enter = panel.Find("#sign-in-password").KeyDownAsync(new KeyboardEventArgs { Key = "Enter" });
        await panel.Find("#sign-in-password").ChangeAsync(new ChangeEventArgs { Value = "pandesal 7am" });
        settings.Hold.SetResult();
        await enter;

        panel.WaitForAssertion(() => panel.Find(".sign-in-error").TextContent.Should().Be("That isn't the password."), Hashing);
        signIn.Password.Should().BeEmpty();
        panel.Find("#sign-in-password").GetAttribute("value").Should().BeEmpty();
    }

    [Test]
    public async Task Locked_after_a_sign_in_the_panel_says_the_window_is_locked()
    {
        var settings = new InMemoryAccessSettings();
        await settings.CreatePasswordAsync("pandesal 6am", CancellationToken.None);
        await using var context = NewContext(settings);
        var appLock = context.Services.GetRequiredService<AppLock>();
        appLock.Unlock();
        appLock.Lock();

        var panel = context.Render<SignInScreen>();

        panel.WaitForAssertion(() => panel.Find(".caption").TextContent.Should().StartWith("The window is locked."));
    }

    [Test]
    public async Task The_panel_left_is_let_go_though_its_ViewModel_and_the_lock_live_on()
    {
        var settings = new InMemoryAccessSettings();
        await using var context = NewContext(settings);

        // No WaitForAssertion on the panel: bUnit's wait would hold it. The in-memory password reads at once.
        var left = context.ShowAndLeave<SignInScreen>(
            whileShown: panel => panel.FindAll("input[type=password]").Should().HaveCount(2));

        left.IsCollected().Should().BeTrue("the sign-in ViewModel lives as long as the page, and must hold on to none of the panels drawn over it");
    }

    [Test]
    public async Task Before_the_first_sign_in_no_screen_is_built_and_the_title_bar_keeps_only_the_windows_buttons()
    {
        await using var context = NewContext(new InMemoryAccessSettings());
        var appLock = context.Services.GetRequiredService<AppLock>();

        var layout = context.Render<MainLayout>(parameters => parameters.Add(l => l.Body, Screen));

        layout.FindComponents<Probe>().Should().BeEmpty("nothing of the screens is drawn, or loaded, while the window is locked at first");
        layout.FindComponents<SignInScreen>().Should().ContainSingle();
        layout.FindAll(".app-titlebar button").Select(b => b.GetAttribute("aria-label")).Should().Equal("Minimize", "Maximize", "Close");

        await context.Renderer.Dispatcher.InvokeAsync(appLock.Unlock);

        layout.WaitForAssertion(() => layout.FindComponents<Probe>().Should().ContainSingle());
        layout.FindComponents<SignInScreen>().Should().BeEmpty();
        layout.Find(".app-screen").HasAttribute("inert").Should().BeFalse();
        layout.FindAll(".app-titlebar button").Select(b => b.GetAttribute("aria-label")).Should().Contain("Lock");
    }

    [Test]
    public async Task Locked_after_a_sign_in_the_screen_stays_as_it_was_out_of_reach_under_the_panel()
    {
        await using var context = NewContext(new InMemoryAccessSettings());
        var appLock = context.Services.GetRequiredService<AppLock>();
        appLock.Unlock();
        var layout = context.Render<MainLayout>(parameters => parameters.Add(l => l.Body, Screen));
        var screen = layout.FindComponent<Probe>().Instance;

        await layout.Find(".app-titlebar button[aria-label='Lock']").ClickAsync(new MouseEventArgs());

        layout.WaitForAssertion(() => layout.FindComponents<SignInScreen>().Should().ContainSingle());
        appLock.IsLocked.Should().BeTrue();
        layout.FindComponent<Probe>().Instance.Should().BeSameAs(screen, "the screen isn't built again, so its unsaved edits stay");
        layout.Find(".app-screen").HasAttribute("inert").Should().BeTrue();
        layout.FindAll(".app-titlebar button").Select(b => b.GetAttribute("aria-label")).Should().Equal("Minimize", "Maximize", "Close");

        await context.Renderer.Dispatcher.InvokeAsync(appLock.Unlock);

        layout.WaitForAssertion(() => layout.FindComponents<SignInScreen>().Should().BeEmpty());
        layout.FindComponent<Probe>().Instance.Should().BeSameAs(screen);
        layout.Find(".app-screen").HasAttribute("inert").Should().BeFalse();
    }

    [Test]
    public async Task No_move_to_another_address_while_locked()
    {
        await using var context = NewContext(new InMemoryAccessSettings());
        var appLock = context.Services.GetRequiredService<AppLock>();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        appLock.Unlock();
        context.Render<MainLayout>(parameters => parameters.Add(l => l.Body, Screen));

        await context.Renderer.Dispatcher.InvokeAsync(appLock.Lock);
        await context.Renderer.Dispatcher.InvokeAsync(() => navigation.NavigateTo("/products"));
        navigation.Uri.Should().NotEndWith("/products");

        await context.Renderer.Dispatcher.InvokeAsync(appLock.Unlock);
        await context.Renderer.Dispatcher.InvokeAsync(() => navigation.NavigateTo("/products"));
        navigation.Uri.Should().EndWith("/products");
    }

    private static readonly RenderFragment Screen = builder =>
    {
        builder.OpenComponent<Probe>(0);
        builder.CloseComponent();
    };

    /// <summary>A screen, standing in for the one at the address.</summary>
    private sealed class Probe : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddMarkupContent(0, "<p class=\"probe\">A screen</p>");
    }
}
