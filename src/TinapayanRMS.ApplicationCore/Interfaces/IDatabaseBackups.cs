namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// Backups of the app's database, as PostgreSQL's own backup files (pg_dump's custom format) in one folder (Settings →
/// Database, and the day's automatic one at startup). Taking one never changes the database; restoring one
/// replaces everything in it, after a safety backup of what was there.
/// </summary>
public interface IDatabaseBackups
{
    /// <summary>Where the backups are kept.</summary>
    string Folder { get; }

    /// <summary>Every backup in <see cref="Folder"/>, newest first; none while the folder doesn't exist yet.</summary>
    IReadOnlyList<DatabaseBackup> List();

    /// <summary>The newest backup in <see cref="Folder"/> taken of this database (BackupRules.Newest); null when it has none.</summary>
    Task<DatabaseBackup?> NewestAsync(CancellationToken cancellationToken);

    /// <summary>Backs the database up now, to a new file in <see cref="Folder"/>.</summary>
    Task<DatabaseBackup> BackUpAsync(BackupKind kind, CancellationToken cancellationToken);

    /// <summary>
    /// The day's automatic backup, unless one was taken on <paramref name="now"/>'s day already: null then. The oldest
    /// automatic backups beyond the ones kept are deleted (BackupRules.AutomaticBackupsKept).
    /// </summary>
    Task<DatabaseBackup?> BackUpDailyIfDueAsync(DateTime now, CancellationToken cancellationToken);

    /// <summary>Copies a backup file from elsewhere (a USB stick, another computer) into <see cref="Folder"/>, to restore it from there.</summary>
    Task<DatabaseBackup> CopyInAsync(string sourcePath, CancellationToken cancellationToken);

    /// <summary>Copies a backup out of <see cref="Folder"/> to <paramref name="destinationPath"/>, replacing a file there.</summary>
    Task CopyToAsync(DatabaseBackup backup, string destinationPath, CancellationToken cancellationToken);

    /// <summary>What a file holds, read from the file itself; a BusinessRuleException when it isn't a full database backup.</summary>
    Task<BackupHeader> InspectAsync(DatabaseBackup backup, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces the database with <paramref name="backup"/>, after a safety backup of it as it is. A backup from an earlier
    /// version of the app is brought up to date afterwards; one from a later version, or of another app's database, is
    /// refused. Whatever goes wrong, the safety backup is put back.
    /// </summary>
    Task<RestoredBackup> RestoreAsync(DatabaseBackup backup, CancellationToken cancellationToken);

    /// <summary>Deletes a backup file: one copied in to restore and then refused or not used.</summary>
    void Delete(DatabaseBackup backup);
}

/// <summary>What made a backup: Back Up Now, the day's automatic one, the safety copy a restore takes first, or a file brought in to restore.</summary>
public enum BackupKind
{
    Manual,
    Automatic,
    BeforeRestore,
    CopiedIn,
}

/// <summary>
/// A backup file: where it is, the database it's of (from its name; null for a file the app didn't name), what made it,
/// when, and how big it is.
/// </summary>
public sealed record DatabaseBackup(string Path, string? DatabaseName, BackupKind Kind, DateTime TakenAt, long SizeBytes);

/// <summary>What a backup file says of itself: the database it's of, and when it was taken.</summary>
public sealed record BackupHeader(string DatabaseName, DateTime TakenAt);

/// <summary>A restore that went through: the safety backup taken before it, and how many app updates the backup needed.</summary>
public sealed record RestoredBackup(DatabaseBackup SafetyBackup, int UpdatesApplied);
