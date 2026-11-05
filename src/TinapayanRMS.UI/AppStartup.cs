using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.UI.ViewModels;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI;

/// <summary>
/// What the app does when it can't start: it logs why, says why, and closes. A failure before the main window shows
/// (appsettings.json missing, a screen that won't build) mustn't reach App's handler for exceptions on the UI thread,
/// which is for a running app: it would say "The app is still running" and keep the process alive, with no window to
/// close it from. UnhandledErrorGuard decides what happens once the window is up, where there's unsaved work to carry on
/// with.
/// </summary>
public static class AppStartup
{
    /// <summary>What the process exits with when it couldn't start.</summary>
    public const int FailedExitCode = 1;

    /// <summary>
    /// Runs <paramref name="start"/>, and returns whether it went through. Otherwise the failure is logged,
    /// <paramref name="say"/> is given what to tell the user, and <paramref name="shutdown"/> is given the exit code.
    /// </summary>
    public static bool TryStart(Action start, string logFolder, ILogger logger, Action<string> say, Action<int> shutdown)
    {
        try
        {
            start();
            return true;
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "The app couldn't start");
            say(FailureText(ex, logFolder));
            shutdown(FailedExitCode);
            return false;
        }
    }

    /// <summary>
    /// The failure's own words rather than <see cref="Failure"/>'s, which are for a screen the user can carry on from: at
    /// startup there's nothing to put right, and whoever looks into it needs the cause.
    /// </summary>
    public static string FailureText(Exception exception, string logFolder) =>
        "TinapayanRMS couldn't start, so it's closing."
        + Environment.NewLine + Environment.NewLine
        + Failure.FirstLine(exception.Message)
        + Environment.NewLine + Environment.NewLine
        + $"The details are in the log, in {logFolder}.";

    /// <summary>What the app says when the database is behind this version of it, by <paramref name="count"/> migrations.</summary>
    public static string PendingMigrationsText(int count) =>
        $"The database is out of date for this version of TinapayanRMS: {count} "
        + $"{(count == 1 ? "update hasn't" : "updates haven't")} been applied to it, so some screens won't work."
        + Environment.NewLine + Environment.NewLine
        + "Close the app and apply them (dotnet ef database update), then open it again.";
}
