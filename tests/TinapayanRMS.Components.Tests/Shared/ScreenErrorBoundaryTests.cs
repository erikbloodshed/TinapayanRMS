using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Serilog.Events;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// ScreenErrorBoundary: a screen that fails gives way to a panel saying so, rather than taking the window's page with it,
/// and opens again as it was, since what it shows is the page's (its state), not the screen's.
/// </summary>
public class ScreenErrorBoundaryTests
{
    /// <summary>What the screen shows, the page's for as long as it lasts, as a screen's state is.</summary>
    public sealed class Shown
    {
        public bool Fails { get; set; }

        public string Name { get; set; } = "Pandesal";
    }

    /// <summary>A screen that throws as it's drawn while it fails.</summary>
    private sealed class Screen : ComponentBase
    {
        [Inject] public Shown Shown { get; set; } = null!;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            if (Shown.Fails)
            {
                throw new InvalidOperationException("Broken drawing");
            }

            builder.OpenElement(0, "p");
            builder.AddContent(1, Shown.Name);
            builder.CloseElement();
        }
    }

    private static (IRenderedComponent<ScreenErrorBoundary> Boundary, Shown Shown, LogEvents Log) RenderFailing(BunitContext context)
    {
        var log = new LogEvents();
        context.Services.AddSingleton(log.Logger);
        context.Services.AddScoped<Shown>();
        var shown = context.Services.GetRequiredService<Shown>();
        shown.Fails = true;
        var boundary = context.Render<ScreenErrorBoundary>(parameters => parameters.AddChildContent<Screen>());
        return (boundary, shown, log);
    }

    [Test]
    public async Task A_screen_that_fails_gives_way_to_a_panel_and_the_failure_is_logged()
    {
        await using var context = AppServices.NewContext();
        var (boundary, _, log) = RenderFailing(context);

        boundary.Find(".screen-error").TextContent.Should().Contain("ran into a problem it didn't expect");
        boundary.FindAll("p").Should().NotContain(p => p.TextContent == "Pandesal");
        log.Events.Should().Contain(e => e.Level == LogEventLevel.Error && e.Exception!.Message == "Broken drawing");
    }

    [Test]
    public async Task Open_It_Again_shows_the_screen_as_it_was_with_what_was_unsaved()
    {
        await using var context = AppServices.NewContext();
        var (boundary, shown, _) = RenderFailing(context);
        shown.Fails = false;
        shown.Name = "Pandesal, not saved yet";

        boundary.Find(".screen-error button").Click();

        boundary.WaitForAssertion(() => boundary.Find("p").TextContent.Should().Be("Pandesal, not saved yet"));
        boundary.FindAll(".screen-error").Should().BeEmpty();
    }

    [Test]
    public async Task Another_address_opens_its_screen_rather_than_the_panel()
    {
        await using var context = AppServices.NewContext();
        var (boundary, shown, _) = RenderFailing(context);
        shown.Fails = false;

        context.Services.GetRequiredService<NavigationManager>().NavigateTo("products");

        boundary.WaitForAssertion(() => boundary.Find("p").TextContent.Should().Be("Pandesal"));
    }
}
