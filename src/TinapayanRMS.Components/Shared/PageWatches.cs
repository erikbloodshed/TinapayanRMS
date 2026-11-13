using Microsoft.JSInterop;

namespace TinapayanRMS.Components.Shared;

/// <summary>Letting go of what a component asked the page to watch for it, as the component goes.</summary>
public static class PageWatches
{
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
