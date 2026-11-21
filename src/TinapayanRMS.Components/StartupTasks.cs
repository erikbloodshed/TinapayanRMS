using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Presentation.App;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components;

/// <summary>
/// What the app does as it starts, once the app is built: the database made ready for this version (DatabaseReadiness,
/// which the sign-in panel waits for too: a restore cut short put back, and the updates this version needs applied after
/// a backup), then the day's automatic backup, checked as the app starts and every hour while it runs, so an app left
/// open for days still takes one a day. Neither holds the window up. A database that can't be made ready shows in the
/// sign-in panel, and in the banner for a check that started signed in; a backup that fails is logged, and once the
/// newest backup is getting old the window's banner says so (AutomaticBackups.OverdueWarningAsync), until one is taken.
/// </summary>
public sealed class StartupTasks(
    DatabaseReadiness readiness,
    AutomaticBackups backups,
    AppStatus status,
    Serilog.ILogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await MakeDatabaseReadyAsync();
        await BackUpDailyAsync(stoppingToken);

        using var hourly = new PeriodicTimer(AutomaticBackups.CheckEvery);
        try
        {
            while (await hourly.WaitForNextTickAsync(stoppingToken))
            {
                await BackUpDailyAsync(stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The app is stopping.
        }
    }

    // Before the day's backup, so the backup is of the database as this version has it. The sign-in panel shows a failure
    // itself, and tries again from there.
    private async Task MakeDatabaseReadyAsync()
    {
        try
        {
            await readiness.EnsureReadyAsync();
        }
        catch (Exception ex)
        {
            var failure = Failure.Of(ex);
            logger.Warning(ex, "The database isn't ready for this version of the app: {Reason}", failure.Text);
            status.WarnOfDatabase(failure.Text);
        }
    }

    // AutomaticBackups logs what it took and why it couldn't, and copies a new one to the second folder.
    private async Task BackUpDailyAsync(CancellationToken cancellationToken)
    {
        try
        {
            await backups.RunAsync(DateTime.Now, cancellationToken);
            if (backups.LastBackupProblem is null)
            {
                status.WarnOfBackups(null);
            }
            else if (await backups.OverdueWarningAsync(DateTime.Now, cancellationToken) is { } warning)
            {
                status.WarnOfBackups(warning);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning(ex, "Couldn't check the day's automatic backup");
        }
    }
}
