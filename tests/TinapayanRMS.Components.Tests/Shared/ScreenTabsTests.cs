using AwesomeAssertions;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>ScreenTabs: which screens have a tab on the strip, and which tab an address belongs to.</summary>
public class ScreenTabsTests
{
    private static string[] Labels(ScreenTabs tabs) => [.. tabs.Open.Select(tab => tab.Label)];

    [Test]
    public void The_first_three_screens_are_open_from_the_start()
    {
        Labels(new ScreenTabs()).Should().Equal("Raw Materials", "Sub-Assemblies", "Products");
    }

    [Test]
    [Arguments("raw-materials", AppScreen.RawMaterials)]
    [Arguments("raw-materials/7", AppScreen.RawMaterials)]
    [Arguments("sub-assemblies/12", AppScreen.SubAssemblies)]
    [Arguments("products", AppScreen.Products)]
    [Arguments("products/42", AppScreen.Products)]
    [Arguments("Products/42?from=reports#top", AppScreen.Products)]
    [Arguments("reports", AppScreen.Reports)]
    [Arguments("price-simulator", AppScreen.PriceSimulator)]
    [Arguments("print", AppScreen.Print)]
    [Arguments("settings", AppScreen.Settings)]
    [Arguments("settings/roles", AppScreen.Settings)]
    public void An_address_belongs_to_its_screens_tab(string path, AppScreen screen)
    {
        ScreenTabs.At(path)!.Screen.Should().Be(screen);
    }

    [Test]
    [Arguments("")]
    [Arguments("products-archive")]
    [Arguments("print/preview")] // What-If's or the Production Plan's own preview, not the Print screen
    public void A_page_that_isnt_a_screen_has_no_tab(string path)
    {
        ScreenTabs.At(path).Should().BeNull();
    }

    [Test]
    public void Showing_a_screen_opens_its_tab_in_the_strips_order()
    {
        var tabs = new ScreenTabs();

        tabs.Show("print")!.Label.Should().Be("Print");
        tabs.Show("reports")!.Label.Should().Be("Reports");
        tabs.Show("products/42");
        tabs.Show("settings/roles")!.Label.Should().Be("Settings");
        tabs.Show("print/preview").Should().BeNull();

        Labels(tabs).Should().Equal("Raw Materials", "Sub-Assemblies", "Products", "Reports", "Print", "Settings");
    }

    [Test]
    public void Closing_a_tab_gives_the_one_before_it_to_show_instead()
    {
        var tabs = new ScreenTabs();
        var products = tabs.Show("products")!;

        tabs.Close(products)!.Label.Should().Be("Sub-Assemblies");

        Labels(tabs).Should().Equal("Raw Materials", "Sub-Assemblies");
    }

    [Test]
    public void Closing_the_first_tab_gives_the_one_after_it()
    {
        var tabs = new ScreenTabs();

        tabs.Close(tabs.Show("raw-materials")!)!.Label.Should().Be("Sub-Assemblies");
    }

    [Test]
    public void The_last_tab_stays()
    {
        var tabs = new ScreenTabs();
        tabs.Close(tabs.Show("raw-materials")!);
        tabs.Close(tabs.Show("sub-assemblies")!);
        tabs.CanClose.Should().BeFalse();

        tabs.Close(tabs.Show("products")!).Should().BeNull();

        Labels(tabs).Should().Equal("Products");
    }

    [Test]
    public void A_closed_tab_comes_back_when_its_screen_is_shown_again()
    {
        var tabs = new ScreenTabs();
        tabs.Close(tabs.Show("sub-assemblies")!);
        Labels(tabs).Should().Equal("Raw Materials", "Products");

        tabs.Show("sub-assemblies/3");

        Labels(tabs).Should().Equal("Raw Materials", "Sub-Assemblies", "Products");
    }

    [Test]
    public void Closing_a_tab_already_closed_changes_nothing()
    {
        var tabs = new ScreenTabs();
        var reports = ScreenTabs.At("reports")!;

        tabs.Close(reports).Should().BeNull();

        Labels(tabs).Should().Equal("Raw Materials", "Sub-Assemblies", "Products");
    }
}
