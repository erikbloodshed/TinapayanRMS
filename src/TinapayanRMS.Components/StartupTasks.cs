using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.Presentation.ViewModels;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components;

/// <summary>
/// What the app does as it starts, once the app is built: a warning when the database lacks updates this version needs
/// (`dotnet ef database update` applies them), and the day's automatic backup, checked as the app starts and every hour
/// while it runs, so an app left open for days still takes one a day.
/// Neither holds the app up. A backup that fails is logged, and once the newest backup is getting old the window's banner
/// says so (AutomaticBackups.OverdueWarningAsync), until one is taken.
/// </summary>
public sealed class StartupTasks(
    IDbContextFactory<TinapayanRMSDbContext> dbContextFactory,
    IDatabaseBackups databaseBackups,
    AutomaticBackups backups,
    AppStatus status,
    Serilog.ILogger logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedRestoreAsync(stoppingToken);
        await WarnOfPendingMigrationsAsync(stoppingToken);
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

    // A restore the app ended in the middle of (the window can't be closed during one, but the power can go) can leave the
    // database under another name, and every screen unable to load: put back first, before anything reads it.
    private async Task RecoverInterruptedRestoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (await databaseBackups.RecoverInterruptedRestoreAsync(cancellationToken))
            {
                logger.Warning(
                    "A restore was cut short as it put the restored copy in place: the database was put back as it was before the restore");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning(ex, "Couldn't check for a restore cut short");
        }
    }

    private async Task WarnOfPendingMigrationsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> pending;
        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            pending = [.. await dbContext.Database.GetPendingMigrationsAsync(cancellationToken)];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.Warning(ex, "Couldn't check the database for migrations not applied yet");
            return;
        }

        if (pending.Count == 0)
        {
            return;
        }

        logger.Warning("The database is missing {Count} migrations: {Migrations}", pending.Count, pending);
        status.WarnOfDatabase(
            $"The database is out of date for this version of TinapayanRMS: {pending.Count} "
            + $"{(pending.Count == 1 ? "update hasn't" : "updates haven't")} been applied to it, so some screens won't work. "
            + "Stop the app and apply them (dotnet ef database update), then start it again.");
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
