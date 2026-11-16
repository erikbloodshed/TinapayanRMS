using AwesomeAssertions;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Components.Tests.Shared;

/// <summary>
/// Each of the app's icons is one of Syncfusion's, which the theme the page links draws: checked against the theme at
/// SyncfusionVersion, so an upgrade that renames or drops an icon fails here, not as a blank on a screen.
/// </summary>
public class AppIconsTests
{
    [Test]
    public void Every_icon_is_one_the_theme_draws()
    {
        var theme = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fluent2-lite.css"));

        Enum.GetValues<AppIcon>().Should().AllSatisfy(icon =>
        {
            var css = AppIcons.CssOf(icon);
            css.Should().StartWith("glyph e-icons e-");
            theme.Should().MatchRegex($@"\.{css.Split(' ')[^1]}::?before\b", $"the theme draws {icon}");
        });
    }
}
