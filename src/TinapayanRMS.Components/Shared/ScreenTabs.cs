using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Components.Vim;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// A screen's tab: the screen, the name on the tab, and whether the addresses under its own are its too, a record's
/// (products/42) or one of Settings' tabs (settings/roles).
/// </summary>
public sealed record ScreenTab(AppScreen Screen, string Label, bool HasPagesUnder = false)
{
    /// <summary>The screen's address, as the menu's link has it.</summary>
    public string Href => VimKeysState.UrlOf(Screen);
}

/// <summary>
/// The screens' tabs along the top, as the WPF window's, for the window: Raw Materials, Sub-Assemblies and Products
/// are open from the start, the others open when their screen is first shown (from the menu, a link, or Products'
/// Print), and any can be closed but the last. Closing only takes the tab off the strip: the screen keeps what it held,
/// and showing it again brings the tab back. Settings has a tab too, though it was a dialog there: here it's a page
/// like the rest, with tabs of its own under the strip.
/// </summary>
public sealed class ScreenTabs
{
    // The strip's order: the menu's screens, Print, then Settings, last as in the menu.
    private static readonly ScreenTab[] All =
    [
        new(AppScreen.RawMaterials, "Raw Materials", HasPagesUnder: true),
        new(AppScreen.SubAssemblies, "Sub-Assemblies", HasPagesUnder: true),
        new(AppScreen.Products, "Products", HasPagesUnder: true),
        new(AppScreen.Reports, "Reports"),
        new(AppScreen.PriceSimulator, "Price Simulator"),
        new(AppScreen.Print, "Print"),
        new(AppScreen.Settings, "Settings", HasPagesUnder: true),
    ];

    private readonly HashSet<AppScreen> _open = [AppScreen.RawMaterials, AppScreen.SubAssemblies, AppScreen.Products];

    /// <summary>The tabs on the strip, in its order.</summary>
    public IReadOnlyList<ScreenTab> Open => [.. All.Where(tab => _open.Contains(tab.Screen))];

    /// <summary>Whether a tab can be closed: not the last one, since some screen is always showing.</summary>
    public bool CanClose => _open.Count > 1;

    /// <summary>
    /// The tab of the screen at <paramref name="path"/>, an address within the app (<c>products/42</c>), or null for a
    /// page that has none: a preview opened from the Price Simulator.
    /// </summary>
    public static ScreenTab? At(string path)
    {
        var end = path.IndexOfAny(['?', '#']);
        var page = (end < 0 ? path : path[..end]).Trim('/');
        return All.FirstOrDefault(tab => page.Equals(tab.Href, StringComparison.OrdinalIgnoreCase)
            || (tab.HasPagesUnder && page.StartsWith(tab.Href + "/", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>The tab of the screen at <paramref name="path"/>, put back on the strip if it was closed; null for a page with no tab.</summary>
    public ScreenTab? Show(string path)
    {
        var tab = At(path);
        if (tab is not null)
        {
            _open.Add(tab.Screen);
        }

        return tab;
    }

    /// <summary>
    /// Takes <paramref name="tab"/> off the strip and returns the tab to show in its place if it was the one showing:
    /// the one before it, or else the one after. Null, with nothing closed, for the last tab.
    /// </summary>
    public ScreenTab? Close(ScreenTab tab)
    {
        var open = Open.ToList();
        var index = open.IndexOf(tab);
        if (index < 0 || !CanClose)
        {
            return null;
        }

        _open.Remove(tab.Screen);
        return open[index > 0 ? index - 1 : 1];
    }
}
