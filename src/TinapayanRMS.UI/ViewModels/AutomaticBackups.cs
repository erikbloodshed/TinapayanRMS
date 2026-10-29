using System.IO;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// The day's automatic backup (2026-10-29, at the user's request, made safer): taken when the app starts and checked again
/// every hour while it stays open, so a copy left open over midnight still takes the next day's; and copied to a second
/// folder when one is set (a USB stick, a synced cloud folder), which keeps the newest automatic ones as the backups folder
/// does. The second folder is kept for the Windows user on this computer (IViewStateStore). Nothing here says anything on
/// screen: what couldn't be done is logged, and the Database tab shows the copy's last problem.
/// </summary>
public sealed class AutomaticBackups(IDatabaseBackups backups, IViewStateStore viewState, ILogger logger)
{
    /// <summary>The view state key the second folder is kept under.</summary>
    public const string CopyFolderKey = "backup-copy-folder";

    /// <summary>How often a running app checks whether the day's backup is due.</summary>
    public static readonly TimeSpan CheckEvery = TimeSpan.FromHours(1);

    private readonly ILogger _logger = logger.ForContext<AutomaticBackups>();

    /// <summary>Where each automatic backup is copied as well; null for nowhere.</summary>
    public string? CopyFolder
    {
        get => viewState.ReadList(CopyFolderKey) is [{ Length: > 0 } folder] ? folder : null;
        set => viewState.WriteList(CopyFolderKey, value is { Length: > 0 } folder ? [folder] : []);
    }

    /// <summary>Why the last copy to <see cref="CopyFolder"/> didn't go through; null when it did, or none was tried.</summary>
    public string? LastCopyProblem { get; private set; }

    /// <summary>Takes the day's backup if it's due, and copies it to the second folder. The backup taken, or null when none was due.</summary>
    public async Task<DatabaseBackup?> RunAsync(DateTime now, CancellationToken cancellationToken)
    {
        var backup = await backups.BackUpDailyIfDueAsync(now, cancellationToken);
        if (backup is not null)
        {
            _logger.Information("Took the day's automatic backup: {Path}", backup.Path);
            await CopyAsync(backup, cancellationToken);
        }

        return backup;
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
}
