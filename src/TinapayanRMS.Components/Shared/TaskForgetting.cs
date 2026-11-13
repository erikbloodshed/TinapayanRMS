using Microsoft.JSInterop;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// Work a component starts and doesn't wait for: a toast shown from a ViewModel's change, a redraw asked for from a
/// service's event, the Vim keys turned on. Left as a bare discard (<c>_ = ...</c>), what it threw only reached
/// TaskScheduler.UnobservedTaskException when the task was collected, if ever, long after and with nothing saying what it
/// was. Given to <see cref="Forget"/>, it's logged as it fails.
/// </summary>
public static class TaskForgetting
{
    /// <summary>
    /// Lets <paramref name="task"/> run on without waiting for it, logging what it throws. A page that has gone (a reload
    /// took it away) isn't a failure, nor is a cancellation.
    /// </summary>
    public static void Forget(this Task task, ILogger logger)
    {
        if (task.IsCompletedSuccessfully)
        {
            return;
        }

        _ = task.ContinueWith(
            static (failed, state) =>
            {
                var exception = failed.Exception!.GetBaseException();
                if (exception is not (JSDisconnectedException or OperationCanceledException))
                {
                    ((ILogger)state!).Warning(exception, "Something the page started without waiting for it failed: {Message}", exception.Message);
                }
            },
            logger,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }
}
