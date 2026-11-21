using System.Diagnostics;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>Waiting for a screen to settle, and checking what it shows, as the tests do (Presentation.Tests' Settle), on the UI thread.</summary>
public static class Screens
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Until a record view and every tab's form have nothing running, three times over: a view's work runs as the form
    /// showing's, and what one finishes can start more on another (a pick's details, the list's flags). Each wait yields
    /// behind what's queued on the UI thread before it ends (BackgroundWork.WhenIdleAsync).
    /// </summary>
    public static async Task SettleAsync<TForm>(RecordViewState<TForm> view)
        where TForm : ScreenState, ITabForm, IDisposable
    {
        var clock = Stopwatch.StartNew();
        for (var pass = 0; pass < 3; pass++)
        {
            Task[] idle = [view.WhenIdleAsync(), .. view.Tabs.All.Select(tab => tab.Form.WhenIdleAsync())];
            var left = Limit - clock.Elapsed;
            try
            {
                await Task.WhenAll(idle).WaitAsync(left > TimeSpan.Zero ? left : TimeSpan.FromTicks(1));
            }
            catch (TimeoutException)
            {
                throw new TimeoutException($"{view.GetType().Name} was still busy after {Limit.TotalSeconds:0} s.");
            }
        }
    }

    /// <summary>Until <paramref name="screen"/> has nothing running.</summary>
    public static async Task SettleAsync(ScreenState screen)
    {
        await screen.WhenIdleAsync().WaitAsync(Limit);
        await Task.Yield();
        await screen.WhenIdleAsync().WaitAsync(Limit);
    }

    /// <summary>Throws when <paramref name="screen"/> shows a failure: a screen shows what went wrong rather than throwing it.</summary>
    public static void EnsureNoFailure(ScreenState? screen)
    {
        if (screen?.ErrorMessage is { } error)
        {
            throw new InvalidOperationException($"{screen.GetType().Name} shows a failure: {error}");
        }
    }

    /// <summary>Throws with <paramref name="what"/> unless <paramref name="condition"/> holds.</summary>
    public static void Ensure(bool condition, string what)
    {
        if (!condition)
        {
            throw new InvalidOperationException(what);
        }
    }
}
