using System.Reactive.Linq;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// Runs a screen's load each time its tab is shown (MainWindow.LoadSelectedTab), never two of one screen's side by side.
/// One asked for while the screen's last is still running follows it once it's done, if the screen still shows: the
/// running one may have read before another screen changed what it shows. Until 2026-11-05 such a showing was dropped,
/// and the screen kept what the earlier load had read until it was shown again.
/// </summary>
/// <param name="isShowing">Whether a screen is the one showing now.</param>
public sealed class ScreenLoads<TScreen>(Func<TScreen, bool> isShowing)
    where TScreen : notnull
{
    private readonly HashSet<TScreen> _running = [];
    private readonly HashSet<TScreen> _wantedAgain = [];

    /// <summary>
    /// Starts <paramref name="load"/> for <paramref name="screen"/>, or has it follow the one running. A load shows its own
    /// failure (ViewModelBase.LoadSafelyAsync); one that escapes it all the same ReactiveUI has handed to App's handler
    /// already, so it isn't thrown a second time here.
    /// </summary>
    public void Load(TScreen screen, Func<IObservable<RxVoid>> load)
    {
        if (!_running.Add(screen))
        {
            _wantedAgain.Add(screen);
            return;
        }

        load()
            .Finally(() =>
            {
                _running.Remove(screen);
                if (_wantedAgain.Remove(screen) && isShowing(screen))
                {
                    Load(screen, load);
                }
            })
            .Subscribe(_ => { }, _ => { });
    }
}
