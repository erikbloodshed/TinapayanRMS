using AwesomeAssertions;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// Waits for a screen in a test to finish what it started in the background (a pick's load, a table's cost reads),
/// as the app's user just watches it fill in. It awaits the screen's own count of that work (ViewModelBase.WhenIdleAsync)
/// rather than polling for a condition to hold for a while, so a test waits exactly as long as the work takes, and a
/// condition that's still false then fails at once, since nothing left running could make it true.
/// </summary>
public static class Settle
{
    /// <summary>The longest a screen may take to settle before the test fails.</summary>
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(15);

    /// <summary>Waits for <paramref name="screen"/> to settle, then requires <paramref name="condition"/>.</summary>
    /// <param name="describe">What the screen shows instead, for the failure's message.</param>
    public static async Task UntilAsync(ViewModelBase screen, Func<bool> condition, Func<string>? describe = null)
    {
        try
        {
            await screen.WhenIdleAsync().WaitAsync(Limit);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"The screen was still working after {Limit.TotalSeconds:0} s: {describe?.Invoke()}");
        }

        condition().Should().BeTrue($"the screen settled, so what it shows now is final: {describe?.Invoke()}");
    }

    /// <summary>
    /// Polls until <paramref name="condition"/> holds, for what happens on a timer rather than as the screen's work: the
    /// folders left open written to the view state, half a second after the last change (FolderStateWriter).
    /// </summary>
    public static async Task EventuallyAsync(Func<bool> condition, Func<string>? describe = null)
    {
        var deadline = DateTime.UtcNow + Limit;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
            {
                throw new TimeoutException($"It didn't happen within {Limit.TotalSeconds:0} s: {describe?.Invoke()}");
            }

            await Task.Delay(25);
        }
    }

    /// <summary>Does <paramref name="pick"/> (a record picked, as a click would), then waits as <see cref="UntilAsync"/> does.</summary>
    public static Task PickAsync(ViewModelBase screen, Action pick, Func<bool> settled, Func<string>? describe = null)
    {
        pick();
        return UntilAsync(screen, settled, describe);
    }
}
