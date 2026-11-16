namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// The work a screen starts that nothing awaits: the load a pick starts (ViewModelBase.LoadLatest), an action started
/// from a tree's selection or a note typed into a table (ViewModelBase.RunSafelyAsync), a cost a table reads for a line.
/// Each is counted while it runs, so whoever drives the screen can wait for it to settle (<see cref="WhenIdleAsync"/>):
/// the tests do, where in the app the user just watches the screen fill in. A screen owns one, and hands it to the parts
/// that start work of their own.
/// </summary>
public sealed class BackgroundWork
{
    private readonly Lock _gate = new();
    private int _running;
    private TaskCompletionSource? _idle;

    /// <summary>How many pieces of work are running now.</summary>
    public int Running
    {
        get
        {
            lock (_gate)
            {
                return _running;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="work"/>, counted from now until it ends, however it ends. Counted before its first await, so
    /// work started by a handler is running by the time the handler returns.
    /// </summary>
    public async Task RunAsync(Func<Task> work)
    {
        lock (_gate)
        {
            _running++;
        }

        try
        {
            await work();
        }
        finally
        {
            TaskCompletionSource? idle = null;
            lock (_gate)
            {
                if (--_running == 0)
                {
                    idle = _idle;
                    _idle = null;
                }
            }

            idle?.SetResult();
        }
    }

    /// <summary>
    /// Completes once nothing is running, and what the last of it left queued on the screen's thread (the continuations
    /// of its awaits, which can start more) has run too: it yields to the screen's thread once, behind what's queued there.
    /// </summary>
    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task? running;
            lock (_gate)
            {
                running = _running == 0 ? null : (_idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            }

            if (running is not null)
            {
                await running;
                continue;
            }

            await Task.Yield();

            lock (_gate)
            {
                if (_running == 0)
                {
                    return;
                }
            }
        }
    }
}
