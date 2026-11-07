using AwesomeAssertions;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// SelfDrawn: what it holds is drawn once by the page and then draws itself, as Reports' grids do over their TableRows, so
/// the page drawing again (for a button, a toast or a grid's event) doesn't take every grid through its drawing again.
/// </summary>
public class SelfDrawnTests
{
    [Test]
    public async Task The_page_drawing_again_doesnt_draw_it_again()
    {
        await using var context = AppServices.NewContext();
        var drawn = context.Render<SelfDrawn>(parameters => parameters.AddChildContent("<span>As the page first drew it</span>"));

        drawn.Render(parameters => parameters.AddChildContent("<span>As the page draws it now</span>"));

        drawn.Find("span").TextContent.Should().Be("As the page first drew it");
    }

    [Test]
    public async Task What_it_holds_still_draws_itself()
    {
        await using var context = AppServices.NewContext();
        var drawn = context.Render<SelfDrawn>(parameters => parameters.AddChildContent<Tally>());

        drawn.Find("button").Click();
        drawn.Find("button").Click();

        drawn.Find("button").TextContent.Should().Be("2");
    }

    /// <summary>A component that draws itself again on a click, as a grid does when its rows change.</summary>
    private sealed class Tally : ComponentBase
    {
        private int _count;

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenElement(0, "button");
            builder.AddAttribute(1, "onclick", EventCallback.Factory.Create(this, () => _count++));
            builder.AddContent(2, _count);
            builder.CloseElement();
        }
    }
}
