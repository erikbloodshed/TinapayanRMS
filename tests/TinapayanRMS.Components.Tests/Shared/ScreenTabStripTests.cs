using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Layout;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>ScreenTabStrip: the screens' tabs under the header, following the address and closed with their ✕.</summary>
public class ScreenTabStripTests
{
    private static string[] Labels(IRenderedComponent<ScreenTabStrip> strip) => [.. strip.FindAll(".screen-tab-link").Select(link => link.TextContent)];

    private static string? Showing(IRenderedComponent<ScreenTabStrip> strip) => strip.FindAll(".screen-tab.active .screen-tab-link").SingleOrDefault()?.TextContent;

    [Test]
    public async Task The_tab_of_the_screen_in_the_address_is_marked()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("products/42");

        var strip = context.Render<ScreenTabStrip>();

        Labels(strip).Should().Equal("Raw Materials", "Sub-Assemblies", "Products");
        Showing(strip).Should().Be("Products");
        strip.Find(".screen-tab.active .screen-tab-link").GetAttribute("href").Should().Be("products");
    }

    [Test]
    public async Task Going_to_a_screen_opens_its_tab()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("raw-materials");
        var strip = context.Render<ScreenTabStrip>();

        navigation.NavigateTo("reports");

        strip.WaitForAssertion(() => Showing(strip).Should().Be("Reports"));
        Labels(strip).Should().Equal("Raw Materials", "Sub-Assemblies", "Products", "Reports");
    }

    [Test]
    public async Task Settings_has_one_tab_whichever_of_its_own_is_open()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("settings/roles");

        var strip = context.Render<ScreenTabStrip>();

        Labels(strip).Should().Equal("Raw Materials", "Sub-Assemblies", "Products", "Settings");
        Showing(strip).Should().Be("Settings");
        strip.Find(".screen-tab.active .screen-tab-link").GetAttribute("href").Should().Be("settings");
    }

    [Test]
    public async Task A_page_with_no_tab_marks_none()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("print/preview");

        var strip = context.Render<ScreenTabStrip>();

        Labels(strip).Should().Equal("Raw Materials", "Sub-Assemblies", "Products");
        Showing(strip).Should().BeNull();
    }

    [Test]
    public async Task Closing_the_tab_showing_goes_to_the_one_before_it()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("reports");
        var strip = context.Render<ScreenTabStrip>();

        strip.Find("button[aria-label='Close Reports']").Click();

        strip.WaitForAssertion(() => Showing(strip).Should().Be("Products"));
        navigation.ToBaseRelativePath(navigation.Uri).Should().Be("products");
        Labels(strip).Should().Equal("Raw Materials", "Sub-Assemblies", "Products");
    }

    [Test]
    public async Task Closing_another_tab_leaves_the_screen_showing()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("products/42");
        var strip = context.Render<ScreenTabStrip>();

        strip.Find("button[aria-label='Close Raw Materials']").Click();

        Labels(strip).Should().Equal("Sub-Assemblies", "Products");
        Showing(strip).Should().Be("Products");
        navigation.ToBaseRelativePath(navigation.Uri).Should().Be("products/42");
    }

    [Test]
    public async Task The_last_tab_has_no_close_button()
    {
        await using var context = AppServices.NewContext();
        var navigation = context.Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("products");
        var strip = context.Render<ScreenTabStrip>();

        strip.Find("button[aria-label='Close Raw Materials']").Click();
        strip.Find("button[aria-label='Close Sub-Assemblies']").Click();

        Labels(strip).Should().Equal("Products");
        strip.FindAll(".screen-tab-close").Should().BeEmpty();
    }
}
