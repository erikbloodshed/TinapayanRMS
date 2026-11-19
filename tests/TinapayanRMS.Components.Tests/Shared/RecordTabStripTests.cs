using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.Shared;
using TinapayanRMS.Presentation.SubAssemblies;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// RecordTabStrip: a record view's tabs, the one showing marked, the preview tab's name in italics, a dot for unsaved
/// changes, each a link to its address, with its ✕ and a double-click to keep it.
/// </summary>
public class RecordTabStripTests
{
    private static IRenderedComponent<RecordTabStrip<SubAssemblyForm>> Render(BunitContext context, SubAssembliesState view, List<RecordTab<SubAssemblyForm>> closed) =>
        context.Render<RecordTabStrip<SubAssemblyForm>>(parameters => parameters
            .Add(s => s.Tabs, view.Tabs)
            .Add(s => s.AddressOf, tab => $"sub-assemblies/new/{tab.Number}")
            .Add(s => s.Close, tab =>
            {
                closed.Add(tab);
                return Task.CompletedTask;
            })
            .Add(s => s.Keep, view.Keep));

    [Test]
    public async Task The_blank_form_the_view_opens_on_is_its_preview_tab_showing()
    {
        await using var context = AppServices.NewContext();
        var view = context.Services.GetRequiredService<SubAssembliesState>();

        var strip = Render(context, view, []);

        var tab = strip.Find(".record-tab");
        tab.ClassList.Should().Contain(["active", "preview"]);
        strip.Find(".record-tab-link").TextContent.Should().Be("New sub-assembly");
        strip.Find(".record-tab-link").GetAttribute("href").Should().Be("sub-assemblies/new/1");
    }

    [Test]
    public async Task A_tab_with_changes_has_its_dot_and_is_no_longer_in_italics_and_another_opens_beside_it()
    {
        await using var context = AppServices.NewContext();
        var view = context.Services.GetRequiredService<SubAssembliesState>();
        var strip = Render(context, view, []);

        await strip.InvokeAsync(() => view.Form!.EditingName = "Sweet Dough");
        await strip.WaitForAssertionAsync(() => strip.Find(".record-tab").ClassList.Should().Contain("dirty").And.NotContain("preview"));

        await strip.InvokeAsync(() => view.NewCommand.ExecuteAsync());

        await strip.WaitForAssertionAsync(() =>
        {
            var tabs = strip.FindAll(".record-tab");
            tabs.Should().HaveCount(2);
            tabs[0].ClassList.Should().Contain("dirty").And.NotContain("active");
            tabs[1].ClassList.Should().Contain(["active", "preview"]);
        });
    }

    [Test]
    public async Task The_close_button_closes_its_tab_and_a_double_click_keeps_one()
    {
        await using var context = AppServices.NewContext();
        var view = context.Services.GetRequiredService<SubAssembliesState>();
        List<RecordTab<SubAssemblyForm>> closed = [];
        var strip = Render(context, view, closed);

        await strip.Find(".record-tab").DoubleClickAsync(new());
        view.Tabs.Active!.IsPreview.Should().BeFalse();
        await strip.WaitForAssertionAsync(() => strip.Find(".record-tab").ClassList.Should().NotContain("preview"));

        await strip.Find(".record-tab-close").ClickAsync(new());
        closed.Should().Equal(view.Tabs.Active);
    }
}
