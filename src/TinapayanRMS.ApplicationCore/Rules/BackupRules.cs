using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// The database backups' rules (2026-10-03): how a backup file is named and read back, when the day's automatic backup is
/// due and which old ones go, whose account SQL Server writes files as, where a restored backup's files go, and whether a
/// restored database is one this version of the app can use.
/// </summary>
public static class BackupRules
{
    /// <summary>The automatic backups kept: two weeks of them. Manual and safety backups are kept until deleted by hand.</summary>
    public const int AutomaticBackupsKept = 14;

    public const string Extension = ".bak";

    public const string NotADatabaseBackupText = "That file isn't a backup of a whole database, so it can't be restored.";

    public const string FilesDontMatchText = "That backup's files don't match this database's, so it can't be restored here.";

    public const string NotThisAppsText = "That backup isn't of TinapayanRMS's data, so it wasn't restored. Nothing has changed.";

    public const string LaterVersionText =
        "That backup was made by a later version of TinapayanRMS, so this version can't use it. Nothing has changed: update the app, then restore it.";

    /// <summary>To the second, so a file name sorts by when it was taken.</summary>
    private const string TimeFormat = "yyyyMMdd_HHmmss";

    private static readonly Dictionary<BackupKind, string> Suffixes = new()
    {
        [BackupKind.Manual] = "manual",
        [BackupKind.Automatic] = "auto",
        [BackupKind.BeforeRestore] = "before-restore",
        [BackupKind.CopiedIn] = "copied-in",
    };

    /// <summary>"TinapayanRMS_20261003_081512_manual.bak": the database, when, and what made it.</summary>
    public static string FileNameFor(string databaseName, BackupKind kind, DateTime takenAt) =>
        $"{databaseName}_{takenAt.ToString(TimeFormat, CultureInfo.InvariantCulture)}_{Suffixes[kind]}{Extension}";

    /// <summary>
    /// The database, what made it and when, for a file named by <see cref="FileNameFor"/>; false for any other name, such as
    /// a backup made outside the app. Read from the right, since a database's name can hold underscores of its own.
    /// </summary>
    public static bool TryParse(string fileName, out string databaseName, out BackupKind kind, out DateTime takenAt)
    {
        databaseName = string.Empty;
        kind = default;
        takenAt = default;
        if (!fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = fileName[..^Extension.Length].Split('_');
        if (parts.Length < 4)
        {
            return false;
        }

        var suffix = parts[^1];
        var match = Suffixes.FirstOrDefault(pair => pair.Value.Equals(suffix, StringComparison.OrdinalIgnoreCase));
        if (match.Value is null
            || !DateTime.TryParseExact($"{parts[^3]}_{parts[^2]}", TimeFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out takenAt))
        {
            takenAt = default;
            return false;
        }

        databaseName = string.Join('_', parts[..^3]);
        kind = match.Key;
        return true;
    }

    /// <summary>
    /// Whether <paramref name="databaseName"/> has no automatic backup yet on the day of <paramref name="now"/>: the first start
    /// of a day takes one. Another database's backups in the same folder, such as a copy's, don't count.
    /// </summary>
    public static bool IsDailyBackupDue(IEnumerable<DatabaseBackup> backups, string databaseName, DateTime now) =>
        !AutomaticOf(backups, databaseName).Any(backup => backup.TakenAt.Date == now.Date);

    /// <summary>
    /// <paramref name="databaseName"/>'s automatic backups beyond its newest <paramref name="keep"/>, oldest last. Nothing else is
    /// ever deleted: no manual, safety or copied-in backup, and no other database's.
    /// </summary>
    public static IReadOnlyList<DatabaseBackup> AutomaticToDelete(IEnumerable<DatabaseBackup> backups, string databaseName, int keep = AutomaticBackupsKept) =>
        [.. AutomaticOf(backups, databaseName).OrderByDescending(backup => backup.TakenAt).Skip(keep)];

    private static IEnumerable<DatabaseBackup> AutomaticOf(IEnumerable<DatabaseBackup> backups, string databaseName) =>
        backups.Where(backup => backup.Kind == BackupKind.Automatic && string.Equals(backup.DatabaseName, databaseName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The Windows account an instance of SQL Server writes backup files as: its service's own (NT SERVICE\MSSQL$SQLEXPRESS
    /// for the SQLEXPRESS instance, NT SERVICE\MSSQLSERVER for a default one), which SQL Server's setup always gives it,
    /// whatever account the service is set to log on as.
    /// </summary>
    public static string ServiceAccountFor(string? instanceName) =>
        string.IsNullOrEmpty(instanceName) || instanceName.Equals("MSSQLSERVER", StringComparison.OrdinalIgnoreCase)
            ? @"NT SERVICE\MSSQLSERVER"
            : $@"NT SERVICE\MSSQL${instanceName}";

    /// <summary>
    /// Refuses a database on another computer: SQL Server reads and writes backup files on its own computer, where this
    /// app's backups folder isn't.
    /// </summary>
    public static void EnsureLocalServer(string serverMachine, string localMachine)
    {
        if (!serverMachine.Equals(localMachine, StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(
                $"Backups need SQL Server to be on this computer, and the database is on {serverMachine}. Back it up there instead.");
        }
    }

    /// <summary>
    /// Where a backup's two files go when it's restored over this database: its data file onto this database's data file,
    /// its log onto this database's log, so a backup made on another computer, or of a copy, lands where this one is.
    /// The app's database has one of each; a backup or a database with any other layout is refused.
    /// </summary>
    /// <param name="backupFiles">The backup's files by their logical names, as RESTORE FILELISTONLY lists them.</param>
    /// <param name="databaseFiles">This database's files on disk, as sys.master_files lists them.</param>
    public static IReadOnlyList<(string LogicalName, string PhysicalPath)> RestoreMoves(
        IReadOnlyList<(string LogicalName, bool IsLog)> backupFiles,
        IReadOnlyList<(string PhysicalPath, bool IsLog)> databaseFiles)
    {
        if (backupFiles.Count(f => !f.IsLog) != 1 || backupFiles.Count(f => f.IsLog) != 1
            || databaseFiles.Count(f => !f.IsLog) != 1 || databaseFiles.Count(f => f.IsLog) != 1)
        {
            throw new BusinessRuleException(FilesDontMatchText);
        }

        return
        [
            (backupFiles.Single(f => !f.IsLog).LogicalName, databaseFiles.Single(f => !f.IsLog).PhysicalPath),
            (backupFiles.Single(f => f.IsLog).LogicalName, databaseFiles.Single(f => f.IsLog).PhysicalPath),
        ];
    }

    /// <summary>
    /// The app updates (migrations) a restored database still needs, in order: the ones this version has that it hasn't,
    /// which a backup from an earlier version lacks. A database with an update this version doesn't know is from a later
    /// version, and one with none at all isn't this app's; both are refused.
    /// </summary>
    /// <param name="applied">The updates the restored database has.</param>
    /// <param name="known">Every update this version of the app has, in order.</param>
    public static IReadOnlyList<string> UpdatesNeeded(IReadOnlyCollection<string> applied, IReadOnlyList<string> known)
    {
        if (applied.Count == 0)
        {
            throw new BusinessRuleException(NotThisAppsText);
        }

        if (applied.Any(update => !known.Contains(update)))
        {
            throw new BusinessRuleException(LaterVersionText);
        }

        return [.. known.Where(update => !applied.Contains(update))];
    }
}
