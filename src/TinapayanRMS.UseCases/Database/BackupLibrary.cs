using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.UseCases.Database;

/// <summary>
/// The backups folder and what's done with it: Settings' Database tab (list, back up now, copy out, restore, restore from a
/// file) and the day's automatic backup (AutomaticBackups). Taking a backup never changes the database; restoring one
/// replaces everything in it, after a safety backup of what was there. A refusal is a <see cref="BusinessRuleException"/>,
/// in words for the shop's staff.
/// </summary>
public interface IBackupLibrary
{
    /// <summary>Where the backups are kept.</summary>
    string Folder { get; }

    /// <summary>Makes <see cref="Folder"/> if it's missing, so it can be shown before any backup has made it.</summary>
    void EnsureFolder();

    /// <summary>Every backup in <see cref="Folder"/>, newest first; none while the folder doesn't exist yet.</summary>
    IReadOnlyList<DatabaseBackup> List();

    /// <summary>The newest backup of this database in <see cref="Folder"/>; null when it has none.</summary>
    Task<DatabaseBackup?> NewestAsync(CancellationToken cancellationToken);

    /// <summary>Backs the database up now, at the user's ask (Back Up Now), to a new file in <see cref="Folder"/>.</summary>
    Task<DatabaseBackup> BackUpNowAsync(CancellationToken cancellationToken);

    /// <summary>The day's automatic backup, unless one was taken on <paramref name="now"/>'s day already: null then.</summary>
    Task<DatabaseBackup?> BackUpDailyIfDueAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>Copies a backup file from elsewhere into <see cref="Folder"/>, to restore it from there.</summary>
    Task<DatabaseBackup> CopyInAsync(string sourcePath, CancellationToken cancellationToken);

    /// <summary>Copies a backup out to <paramref name="destinationPath"/>, replacing a file there.</summary>
    Task CopyToAsync(DatabaseBackup backup, string destinationPath, CancellationToken cancellationToken);

    /// <summary>What a file holds, read from the file itself; refused when it isn't a full database backup.</summary>
    Task<BackupHeader> InspectAsync(DatabaseBackup backup, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the database with <paramref name="backup"/>, after a safety backup of it as it is, bringing an earlier
    /// version's up to date; refused for a later version's or another app's.
    /// </summary>
    Task<RestoredBackup> RestoreAsync(DatabaseBackup backup, CancellationToken cancellationToken);

    /// <summary>Deletes a backup file: one copied in to restore and then refused or not used.</summary>
    void Delete(DatabaseBackup backup);

    /// <summary>
    /// Proves <paramref name="backup"/> can be restored, by restoring it into a scratch database of its own and dropping it
    /// again, the database untouched (the weekly check, AutomaticBackups).
    /// </summary>
    Task<BackupProof> ProveAsync(DatabaseBackup backup, CancellationToken cancellationToken);
}

/// <inheritdoc cref="IBackupLibrary"/>
public sealed class BackupLibrary(IDatabaseBackups backups) : IBackupLibrary
{
    public string Folder => backups.Folder;

    public void EnsureFolder() => backups.EnsureFolder();

    public IReadOnlyList<DatabaseBackup> List() => backups.List();

    public Task<DatabaseBackup?> NewestAsync(CancellationToken cancellationToken) => backups.NewestAsync(cancellationToken);

    public Task<DatabaseBackup> BackUpNowAsync(CancellationToken cancellationToken) => backups.BackUpAsync(BackupKind.Manual, cancellationToken);

    public Task<DatabaseBackup?> BackUpDailyIfDueAsync(DateTime now, CancellationToken cancellationToken) =>
        backups.BackUpDailyIfDueAsync(now, cancellationToken);

    public Task<DatabaseBackup> CopyInAsync(string sourcePath, CancellationToken cancellationToken) => backups.CopyInAsync(sourcePath, cancellationToken);

    public Task CopyToAsync(DatabaseBackup backup, string destinationPath, CancellationToken cancellationToken) =>
        backups.CopyToAsync(backup, destinationPath, cancellationToken);

    public Task<BackupHeader> InspectAsync(DatabaseBackup backup, CancellationToken cancellationToken) => backups.InspectAsync(backup, cancellationToken);

    public Task<RestoredBackup> RestoreAsync(DatabaseBackup backup, CancellationToken cancellationToken) => backups.RestoreAsync(backup, cancellationToken);

    public void Delete(DatabaseBackup backup) => backups.Delete(backup);

    public Task<BackupProof> ProveAsync(DatabaseBackup backup, CancellationToken cancellationToken) => backups.ProveAsync(backup, cancellationToken);
}
