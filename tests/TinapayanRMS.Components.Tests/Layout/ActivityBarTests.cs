using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Layout;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Layout;

/// <summary>ActivityBar: the screens' icons down the window's left edge, Settings at the foot, the one showing marked.</summary>
public class ActivityBarTests
{
    private static string?[] Marked(IRenderedComponent<ActivityBar> bar) =>
        [.. bar.FindAll(".activity-item.active").Select(link => link.GetAttribute("aria-label"))];

    [Test]
    public async Task It_has_the_screens_then_Settings_at_the_foot()
    {
        await using var context = AppServices.NewContext();

        var bar = context.Render<ActivityBar>();

        bar.FindAll(".activity-item").Select(link => link.GetAttribute("aria-label")).Should().Equal(
            "Raw Materials", "Sub-Assemblies", "Products", "Reports", "Price Simulator", "Settings");
        bar.FindAll(".activity-item").Select(link => link.GetAttribute("data-title")).Should().Equal(
            ["Raw Materials", "Sub-Assemblies", "Products", "Reports", "Price Simulator", "Settings"],
            "the tooltip shows each icon's name from its data-title");
        bar.FindAll(".activity-item[title]").Should().BeEmpty("a title would bring up the browser's own tooltip as well");
        bar.Find(".activity-fill + .activity-item").GetAttribute("href").Should().Be("settings",
            "Settings is on its own at the foot, under the space that pushes it down");
    }

    [Test]
    public async Task The_screen_in_the_address_is_marked_a_records_and_Settings_pages_included()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("products/42");

        var bar = context.Render<ActivityBar>();

        Marked(bar).Should().Equal("Products");

        await bar.InvokeAsync(() => navigation.NavigateTo("settings/roles"));

        await bar.WaitForAssertionAsync(() => Marked(bar).Should().Equal("Settings"));
    }

    [Test]
    public async Task Locked_it_takes_no_click()
    {
        await using var context = AppServices.NewContext();

        var bar = context.Render<ActivityBar>(parameters => parameters.Add(b => b.Locked, true));

        bar.Find(".activity-bar-host").HasAttribute("inert").Should().BeTrue();
    }
}
