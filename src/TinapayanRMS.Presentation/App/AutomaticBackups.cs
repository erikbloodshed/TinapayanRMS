using System.IO;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Infrastructure.Services;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.App;

/// <summary>
/// The day's automatic backup: taken when the app starts and checked again
/// every hour while it stays open, so a copy left open over midnight still takes the next day's; and copied to a second
/// folder when one is set (a USB stick, a synced cloud folder), which keeps the newest automatic ones as the backups folder
/// does. The second folder is kept for the Windows user on this computer (IViewStateStore), and so is what last went wrong,
/// so backups can't stop for weeks with a failure only in the log, or a copy's problem forgotten at the next start. The
/// Database tab shows both problems, and an
/// automatic backup failing with the newest one getting old also says so once a run (<see cref="OverdueWarningAsync"/>).
/// </summary>
public sealed class AutomaticBackups(IDatabaseBackups backups, IViewStateStore viewState, ILogger logger)
{
    /// <summary>The view state key the second folder is kept under.</summary>
    public const string CopyFolderKey = "backup-copy-folder";

    /// <summary>The view state key what went wrong with the last automatic backup is kept under, when something did.</summary>
    public const string BackupProblemKey = "backup-problem";

    /// <summary>The view state key what went wrong with the last copy to the second folder is kept under, when something did.</summary>
    public const string CopyProblemKey = "backup-copy-problem";

    /// <summary>How often a running app checks whether the day's backup is due.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    private readonly ILogger _logger = logger.ForContext<AutomaticBackups>();

    /// <summary>Set once <see cref="OverdueWarningAsync"/> has said so: one message a run is enough, and the hourly checks would repeat it.</summary>
    private bool _hasWarned;

    /// <summary>Where each automatic backup is copied as well; null for nowhere. A new folder, or none, has no problem yet.</summary>
    public string? CopyFolder
    {
        get => Read(CopyFolderKey);
        set
        {
            Write(CopyFolderKey, value);
            LastCopyProblem = null;
        }
    }

    /// <summary>Why the last automatic backup wasn't taken; null when it was, or today's is there already.</summary>
    public string? LastBackupProblem
    {
        get => Read(BackupProblemKey);
        private set => Write(BackupProblemKey, value);
    }

    /// <summary>Why the last copy to <see cref="CopyFolder"/> didn't go through; null when it did, or none was tried.</summary>
    public string? LastCopyProblem
    {
        get => Read(CopyProblemKey);
        private set => Write(CopyProblemKey, value);
    }

    /// <summary>
    /// Takes the day's backup if it's due, and copies it to the second folder. The backup taken, or null when none was due or
    /// it couldn't be taken, which <see cref="LastBackupProblem"/> says and the log has in full.
    /// </summary>
    public async Task<DatabaseBackup?> RunAsync(DateTime now, CancellationToken cancellationToken)
    {
        DatabaseBackup? backup;
        try
        {
            backup = await backups.BackUpDailyIfDueAsync(now, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastBackupProblem = $"The automatic backup of {now:MMM d, h:mm tt} wasn't taken. {Failure.Of(ex).Text}";
            _logger.Warning(ex, "Couldn't take the day's automatic backup");
            return null;
        }

        LastBackupProblem = null;
        if (backup is not null)
        {
            _logger.Information("Took the day's automatic backup: {Path}", backup.Path);
            await CopyAsync(backup, cancellationToken);
        }

        return backup;
    }

    /// <summary>
    /// What to tell the user when the last automatic backup failed and the newest one there is has got old
    /// (BackupRules.IsOverdue), once a run; null otherwise, as on most days. A single failure doesn't interrupt anyone, since
    /// the next check may well put it right.
    /// </summary>
    public async Task<string?> OverdueWarningAsync(DateTime now, CancellationToken cancellationToken)
    {
        if (_hasWarned || LastBackupProblem is not { } problem)
        {
            return null;
        }

        var newest = await backups.NewestAsync(cancellationToken);
        if (!BackupRules.IsOverdue(newest, now))
        {
            return null;
        }

        _hasWarned = true;
        var since = newest is null ? "There's no backup of the app's data yet." : $"The newest backup of the app's data is from {newest.TakenAt:MMMM d, yyyy}.";
        return since + " Automatic backups aren't working, so what's saved now isn't backed up."
            + Environment.NewLine + Environment.NewLine
            + problem
            + Environment.NewLine + Environment.NewLine
            + "Ask whoever set up the app to look into it. The Database tab in Settings lists the backups, and its Back Up Now tries again.";
    }

    /// <summary>
    /// Copies <paramref name="backup"/> to the second folder, under its own name, then deletes the oldest automatic ones there
    /// beyond the number kept (BackupRules.AutomaticToDelete). A folder that isn't there (a USB stick taken out) is passed over.
    /// </summary>
    public async Task CopyAsync(DatabaseBackup backup, CancellationToken cancellationToken)
    {
        if (CopyFolder is not { } folder)
        {
            return;
        }

        if (!Directory.Exists(folder))
        {
            LastCopyProblem = $"The folder {folder} wasn't there, so the backup of {backup.TakenAt:MMM d, h:mm tt} wasn't copied to it.";
            _logger.Warning("Couldn't copy the automatic backup to {Folder}: it isn't there", folder);
            return;
        }

        try
        {
            await backups.CopyToAsync(backup, Path.Combine(folder, Path.GetFileName(backup.Path)), cancellationToken);
            foreach (var old in BackupRules.AutomaticToDelete(BackupsIn(folder), backup.DatabaseName ?? string.Empty))
            {
                File.Delete(old.Path);
            }

            LastCopyProblem = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LastCopyProblem = $"The backup of {backup.TakenAt:MMM d, h:mm tt} couldn't be copied to {folder}: {ex.Message}";
            _logger.Warning(ex, "Couldn't copy the automatic backup to {Folder}", folder);
        }
    }

    /// <summary>The app's backups in <paramref name="folder"/>, by their names; anything else there is left alone.</summary>
    private static IEnumerable<DatabaseBackup> BackupsIn(string folder) =>
        Directory.EnumerateFiles(folder, "*" + BackupRules.Extension)
            .Select(path => BackupRules.TryParse(Path.GetFileName(path), out var databaseName, out var kind, out var takenAt)
                ? new DatabaseBackup(path, databaseName, kind, takenAt, 0)
                : null)
            .OfType<DatabaseBackup>();

    private string? Read(string key) => viewState.ReadList(key) is [{ Length: > 0 } value] ? value : null;

    /// <summary>Written only when it changes: most hours, nothing has.</summary>
    private void Write(string key, string? value)
    {
        if (Read(key) != value)
        {
            viewState.WriteList(key, value is { Length: > 0 } text ? [text] : []);
        }
    }
}
