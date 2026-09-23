using ReactiveUI;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>The main window's screens, one per tab: what Back and Forward move between.</summary>
public enum AppScreen
{
    RawMaterials,
    SubAssemblies,
    Products,
    Reports,
}

/// <summary>
/// One stop in the navigation history: a screen, and the record it had open when it was left — a product, a
/// sub-assembly or a raw material, by id — or null for none (a new form, or a screen with no record, Reports).
/// </summary>
public sealed record ScreenVisit(AppScreen Screen, int? RecordId);

/// <summary>
/// The main window's Back and Forward, as a browser has them. A step is a change of screen, however it happens:
/// the menu, the tab strip, or a link from one screen to another (a Used By tile, a linked badge). Picking another
/// record in a screen's list isn't one. Each stop remembers the record its screen had open as it was left, so going
/// back to it reopens that record. It knows nothing of tabs or screens' view models: MainWindow hands it visits.
/// </summary>
/// <param name="depth">How many stops Back keeps; the oldest goes when a new one passes it.</param>
public sealed class NavigationHistory(int depth = 50) : ReactiveObject
{
    // Lists rather than Stacks: capping the depth means dropping from the far end, which a Stack can't do.
    private readonly List<ScreenVisit> _back = [];
    private readonly List<ScreenVisit> _forward = [];

    /// <summary>The screen showing, as it was when it was reached; its record is only known once it's left.</summary>
    public ScreenVisit? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;
    public bool CanGoForward => _forward.Count > 0;

    /// <summary>The screen showing when the window opens: where the history starts, with nothing either side of it.</summary>
    public void Start(ScreenVisit visit)
    {
        _back.Clear();
        _forward.Clear();
        Current = visit;
        RaiseCanGo();
    }

    /// <summary>
    /// A move to another screen, not by Back or Forward: <paramref name="leaving"/>, with its record as it stands
    /// now, becomes the stop Back returns to, and what Forward held goes, as a browser's does after a new link.
    /// A move to the screen already showing isn't a step.
    /// </summary>
    public void Visit(ScreenVisit leaving, AppScreen arriving)
    {
        if (arriving == leaving.Screen)
        {
            return;
        }

        Push(_back, leaving);
        _forward.Clear();
        Current = new ScreenVisit(arriving, null);
        RaiseCanGo();
    }

    /// <summary>
    /// Steps back: <paramref name="leaving"/>, with its record as it stands now, goes onto Forward, and the stop
    /// before it is returned for the window to show. Null when there's nowhere to go back to.
    /// </summary>
    public ScreenVisit? GoBack(ScreenVisit leaving) => Step(from: _back, to: _forward, leaving);

    /// <summary>Steps forward into what Back last stepped off, as <see cref="GoBack"/> does the other way. Null when there's nothing there.</summary>
    public ScreenVisit? GoForward(ScreenVisit leaving) => Step(from: _forward, to: _back, leaving);

    private ScreenVisit? Step(List<ScreenVisit> from, List<ScreenVisit> to, ScreenVisit leaving)
    {
        if (from.Count == 0)
        {
            return null;
        }

        var target = from[^1];
        from.RemoveAt(from.Count - 1);
        Push(to, leaving);
        Current = target;
        RaiseCanGo();
        return target;
    }

    private void Push(List<ScreenVisit> stops, ScreenVisit visit)
    {
        stops.Add(visit);
        if (stops.Count > depth)
        {
            stops.RemoveAt(0);
        }
    }

    private void RaiseCanGo()
    {
        this.RaisePropertyChanged(nameof(CanGoBack));
        this.RaisePropertyChanged(nameof(CanGoForward));
    }
}
