using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using TinapayanRMS.Components.Pages.RawMaterials;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;
using TinapayanRMS.Presentation.RawMaterials;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// Raw Materials' Price History tab, whose table a visit to the screen mustn't make while the tab is hidden: Syncfusion's grid
/// then waits to be shown, polling for it, and one never shown crashes as it's let go and leaves the timer running.
/// </summary>
public class PriceHistoryTests
{
    [Test]
    public async Task The_table_is_made_the_first_time_its_tab_shows_and_kept_after()
    {
        await using var context = AppServices.NewContext();
        var state = context.Services.GetRequiredService<RawMaterialsState>();

        var tab = context.Render<PriceHistory>(parameters => parameters.Add(p => p.State, state).Add(p => p.IsShowing, false));
        tab.FindComponents<PickGrid<RawMaterialPriceRow>>().Should().BeEmpty("the tab hasn't been shown");

        tab.Render(parameters => parameters.Add(p => p.State, state).Add(p => p.IsShowing, true));
        tab.FindComponents<PickGrid<RawMaterialPriceRow>>().Should().ContainSingle();

        tab.Render(parameters => parameters.Add(p => p.State, state).Add(p => p.IsShowing, false));
        tab.FindComponents<PickGrid<RawMaterialPriceRow>>().Should().ContainSingle("a grid made while showing lets go cleanly");
    }
}
