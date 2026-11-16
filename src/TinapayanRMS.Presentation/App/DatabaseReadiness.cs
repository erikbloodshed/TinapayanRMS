using TinapayanRMS.ApplicationCore.Interfaces;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.App;

/// <summary>
/// The database made ready once per start, before anything reads it (IDatabaseUpdater: a restore cut short put back, then
/// the database made if it isn't there, backed up and updated if it's behind, refused if a newer copy updated it). The
/// sign-in panel waits for it before it reads the password (SignInState), and StartupTasks before the day's backup;
/// both share the one run. A run that failed is run again by the next ask (the panel's Try Again). One for the process.
/// </summary>
public sealed class DatabaseReadiness(IDatabaseUpdater updater, ILogger logger)
{
    private readonly Lock _lock = new();
    private Task? _ready;
    private bool _isUpdating;

    /// <summary>Raised, on whichever thread, when <see cref="IsUpdating"/> changes.</summary>
    public event Action? Changed;

    /// <summary>Whether updates are being applied (a backup first): the first start after an update takes a while then.</summary>
    public bool IsUpdating
    {
        get => Volatile.Read(ref _isUpdating);
        private set
        {
            Volatile.Write(ref _isUpdating, value);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Completes once the database is ready; fails with why it isn't (a refusal, or the database out of reach), which the
    /// caller shows and logs, and the next call tries again.
    /// </summary>
    public Task EnsureReadyAsync()
    {
        lock (_lock)
        {
            if (_ready is null || _ready.IsFaulted || _ready.IsCanceled)
            {
                _ready = Task.Run(PrepareAsync);
            }

            return _ready;
        }
    }

    private async Task PrepareAsync()
    {
        try
        {
            var update = await updater.UpdateAsync(count =>
            {
                logger.Information("The database needs {Count} updates for this version of the app: applying them", count);
                IsUpdating = true;
            }, CancellationToken.None);

            if (update.RecoveredRestore)
            {
                logger.Warning(
                    "A restore was cut short as it put the restored copy in place: the database was put back as it was before the restore");
            }

            if (update.Created)
            {
                logger.Information("The database wasn't there: made it, with {Count} updates", update.Applied);
            }
            else if (update.Applied > 0)
            {
                logger.Information(
                    "Brought the database up to this version: {Count} updates, after a backup to {Backup}",
                    update.Applied,
                    update.Backup?.Path ?? "(none: it was empty)");
            }
        }
        finally
        {
            if (IsUpdating)
            {
                IsUpdating = false;
            }
        }
    }
}
