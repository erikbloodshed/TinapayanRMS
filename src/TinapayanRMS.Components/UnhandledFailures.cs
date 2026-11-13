using Serilog;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Components;

/// <summary>
/// What fails with nothing there to catch it, which would otherwise leave no trace in the app's log. Three kinds:
/// <list type="bullet">
/// <item>an exception that ends the process (AppDomain.UnhandledException), logged as fatal;</item>
/// <item>a task that failed with nothing awaiting it (TaskScheduler.UnobservedTaskException), logged as a warning, and
/// observed so it ends nothing;</item>
/// <item>a ReactiveUI pipeline that failed (a command whose body threw, or a ToProperty whose source errored) with nothing
/// subscribed to its ThrownExceptions, which ReactiveUI hands to its default handler (ReactiveUIBootstrapper). Logged as an
/// error, and <see cref="Reported"/> so the page shows it as an error toast, where ReactiveUI's own handler threw it on
/// whatever thread it was on (the page's error bar on the window's thread, the end of the process on any other).</item>
/// </list>
/// The screens' own work goes through RunSafelyAsync and its like, which show what fails in plain words: this is what's
/// behind them, for whatever they don't cover. The MAUI app also hooks WinUI's own UnhandledException (its App).
/// </summary>
public static class UnhandledFailures
{
    private static int _watching;

    /// <summary>A ReactiveUI pipeline failed, on whichever thread it failed on: the page's notices show it.</summary>
    public static event Action<Exception>? Reported;

    /// <summary>Logs, from now on, what ends the process and what failed in a task nothing awaited. Once per process.</summary>
    public static void Watch()
    {
        if (Interlocked.Exchange(ref _watching, 1) == 1)
        {
            return;
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            LogUnhandled(Log.Logger, e.ExceptionObject, e.IsTerminating);
            if (e.IsTerminating)
            {
                Log.CloseAndFlush();
            }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => LogUnobserved(Log.Logger, e);
    }

    /// <summary>ReactiveUI's handler for a pipeline that failed with nothing to take its failure (ReactiveUIBootstrapper).</summary>
    public static void Report(Exception exception) => Report(Log.Logger, exception);

    /// <summary>
    /// Logs <paramref name="exception"/> with its stack, then tells the page. Never throws: ReactiveUI calls it from inside
    /// the pipeline that failed, where a throw is what this is here to stop.
    /// </summary>
    public static void Report(ILogger logger, Exception exception)
    {
        try
        {
            logger.Error(exception, "A command or a worked-out property failed, and nothing on the screen caught it");
            Reported?.Invoke(exception);
        }
        catch (Exception ex)
        {
            logger.Warning(ex, "Couldn't show a failure nothing on the screen caught");
        }
    }

    /// <summary>An exception nothing caught, as AppDomain.UnhandledException hands it on.</summary>
    public static void LogUnhandled(ILogger logger, object? exception, bool terminating) =>
        logger.Fatal(
            exception as Exception,
            "An exception nothing caught (the app is ending: {Ending}): {Thrown}",
            terminating,
            exception?.GetType().Name);

    /// <summary>A task that failed with nothing awaiting it: logged, and observed, so it ends nothing.</summary>
    public static void LogUnobserved(ILogger logger, UnobservedTaskExceptionEventArgs e)
    {
        logger.Warning(e.Exception, "A task failed and nothing awaited it");
        e.SetObserved();
    }
}
