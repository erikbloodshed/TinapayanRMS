using Microsoft.JSInterop;

namespace TinapayanRMS.Components.Shared;

/// <summary>Letting go of what a component asked the page to watch for it, as the component goes.</summary>
public static class PageWatches
{
    /// <summary>
    /// The watch <paramref name="registering"/> hands back (app.js's watchTitleBar, registerUndoKeys and the rest), to keep
    /// until the component goes; or, when it went while the page was still being asked (<paramref name="gone"/>: left at
    /// once, by a quick Back or the Vim keys' gt), stopped there and then, and null. Kept regardless, a watch on the
    /// document outlived the component, its disposed reference still called on every key it watched.
    /// </summary>
    public static async ValueTask<IJSObjectReference?> KeepUnlessGoneAsync(this ValueTask<IJSObjectReference> registering, Func<bool> gone)
    {
        var watch = await registering;
        if (!gone())
        {
            return watch;
        }

        await watch.StopAsync();
        return null;
    }

    /// <summary>
    /// Stops <paramref name="watch"/>, an object a page function returned whose dispose() stops it (app.js's watchTitleBar,
    /// watchLineDrag, registerUndoKeys and the rest), and lets go of the reference. Nothing to do when there's none, or
    /// when the page has gone already.
    /// </summary>
    public static async ValueTask StopAsync(this IJSObjectReference? watch)
    {
        if (watch is null)
        {
            return;
        }

        try
        {
            await watch.InvokeVoidAsync("dispose");
            await watch.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The page has gone: nothing left to let go of.
        }
    }
}
