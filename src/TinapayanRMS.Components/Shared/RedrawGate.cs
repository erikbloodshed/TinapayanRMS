namespace TinapayanRMS.Components.Shared;

/// <summary>
/// Merges the redraws a component is asked for into one. A screen state's load sets hundreds of properties, list rows and
/// commands in a row, and a redraw asked for on the web view's dispatcher is drawn, and sent to the web view, there and
/// then: a product took some 445 redraws of its whole screen to open (2026-10-11). The first request since the last
/// redraw queues one behind whatever the dispatcher is running, the rest find it queued, and it draws what they all
/// left. So a redraw follows a change by one turn of the dispatcher, never by a timer.
/// </summary>
/// <param name="invokeAsync">The component's InvokeAsync: a change can come from any thread.</param>
/// <param name="redraw">The component's StateHasChanged.</param>
public sealed class RedrawGate(Func<Func<Task>, Task> invokeAsync, Action redraw)
{
    private int _queued;
    private int _wanted;

    /// <summary>Whether a redraw is on its way, so one asked for now would only be drawn twice.</summary>
    public bool IsPending => Volatile.Read(ref _wanted) != 0;

    /// <summary>Asks for a redraw, which the next turn of the dispatcher draws unless one is already queued.</summary>
    public void Request()
    {
        Volatile.Write(ref _wanted, 1);
        if (Interlocked.Exchange(ref _queued, 1) != 0)
        {
            return;
        }

        _ = invokeAsync(async () =>
        {
            // Behind the work that asked: it's still changing things.
            await Task.Yield();
            Volatile.Write(ref _queued, 0);
            if (Interlocked.Exchange(ref _wanted, 0) != 0)
            {
                redraw();
            }
        });
    }

    /// <summary>
    /// The component is being drawn some other way (by its parent, or at once), so everything asked for so far will be in
    /// it: the redraw queued for those requests is dropped. Called as the drawing starts, before it reads anything
    /// (FolderTree's markup begins with it), never after it: a change on another thread can ask while the component is
    /// drawn, and after the drawing that request would be dropped though the drawing missed it.
    /// </summary>
    public void Drawn() => Volatile.Write(ref _wanted, 0);
}
