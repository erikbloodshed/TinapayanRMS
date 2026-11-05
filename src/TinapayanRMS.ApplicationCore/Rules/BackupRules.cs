using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.ApplicationCore.Rules;

/// <summary>
/// The database backups' rules: how a backup file is named and read back, when the day's automatic backup is
/// due and which old ones go, what a backup file says of itself, and whether a restored database is one this version of the
/// app can use.
/// </summary>
public static class BackupRules
{
    /// <summary>The automatic backups kept: two weeks of them. Manual and safety backups are kept until deleted by hand.</summary>
    public const int AutomaticBackupsKept = 14;

    /// <summary>pg_dump's custom format, the one pgAdmin names .backup too.</summary>
    public const string Extension = ".backup";

    /// <summary>
    /// Added to a backup's name while it's still being written or copied: "….backup.partial". It's never listed, counted as
    /// the day's backup or restored, so a file cut short can't be taken for a whole one.
    /// </summary>
    public const string PartialSuffix = ".partial";

    /// <summary>
    /// How long the database can go without a backup before an automatic backup that fails is worth interrupting someone
    /// for. A failure sooner than this may well put itself right at the next check, and shows on the Database tab anyway.
    /// </summary>
    public static readonly TimeSpan OverdueAfter = TimeSpan.FromDays(3);

    public const string NotADatabaseBackupText = "That file isn't a backup of a whole database, so it can't be restored.";

    public const string NewerPostgresText =
        "That backup was made by a newer version of PostgreSQL than this computer has, so it can't be restored here. Nothing has changed.";

    public const string NotThisAppsText = "That backup isn't of TinapayanRMS's data, so it wasn't restored. Nothing has changed.";

    public const string LaterVersionText =
        "That backup was made by a later version of TinapayanRMS, so this version can't use it. Nothing has changed: update the app, then restore it.";

    /// <summary>To the second, so a file name sorts by when it was taken.</summary>
    private const string TimeFormat = "yyyyMMdd_HHmmss";

    private const string ArchiveCreatedAt = "Archive created at ";

    private const string ArchiveTimeFormat = "yyyy-MM-dd HH:mm:ss";

    private const string DatabaseNameLabel = "dbname:";

    private static readonly FrozenDictionary<BackupKind, string> Suffixes = new Dictionary<BackupKind, string>
    {
        [BackupKind.Manual] = "manual",
        [BackupKind.Automatic] = "auto",
        [BackupKind.BeforeRestore] = "before-restore",
        [BackupKind.CopiedIn] = "copied-in",
    }.ToFrozenDictionary();

    /// <summary>"tinapayanrms_20261003_081512_manual.backup": the database, when, and what made it.</summary>
    public static string FileNameFor(string databaseName, BackupKind kind, DateTime takenAt) =>
        $"{databaseName}_{takenAt.ToString(TimeFormat, CultureInfo.InvariantCulture)}_{Suffixes[kind]}{Extension}";

    /// <summary>
    /// <see cref="FileNameFor"/>'s name, a second later for each one <paramref name="isTaken"/> says is already in the folder.
    /// Names only go to the second, and two backups in the same second mustn't share a file: a restore's safety backup taken
    /// within a second of the one before it would overwrite it, and the restore would then put back the data it was
    /// replacing. Still a name <see cref="TryParse"/> reads, in order.
    /// </summary>
    public static string FreeFileNameFor(string databaseName, BackupKind kind, DateTime takenAt, Func<string, bool> isTaken)
    {
        var name = FileNameFor(databaseName, kind, takenAt);
        while (isTaken(name))
        {
            takenAt = takenAt.AddSeconds(1);
            name = FileNameFor(databaseName, kind, takenAt);
        }

        return name;
    }

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

    /// <summary>
    /// <paramref name="databaseName"/>'s newest backup, of any kind but one copied in, whose name dates it to when it arrived
    /// rather than to the data it holds. Null when it has none. Another database's in the same folder don't count.
    /// </summary>
    public static DatabaseBackup? Newest(IEnumerable<DatabaseBackup> backups, string databaseName) =>
        backups
            .Where(backup => backup.Kind != BackupKind.CopiedIn && string.Equals(backup.DatabaseName, databaseName, StringComparison.OrdinalIgnoreCase))
            .MaxBy(backup => backup.TakenAt);

    /// <summary>Whether <paramref name="newest"/> is too old to go on relying on at <paramref name="now"/>, or there's none at all (<see cref="OverdueAfter"/>).</summary>
    public static bool IsOverdue(DatabaseBackup? newest, DateTime now) =>
        newest is null || now - newest.TakenAt >= OverdueAfter;

    private static IEnumerable<DatabaseBackup> AutomaticOf(IEnumerable<DatabaseBackup> backups, string databaseName) =>
        backups.Where(backup => backup.Kind == BackupKind.Automatic && string.Equals(backup.DatabaseName, databaseName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// What <c>pg_restore --list</c> says a backup file holds, from the comment lines it starts with: the database it was
    /// taken of (";     dbname: tinapayanrms") and when (";     Archive created at 2026-10-10 15:30:12 PST", in the
    /// local time of the computer that took it). False when the listing has neither, which no pg_dump archive lacks.
    /// </summary>
    public static bool TryReadArchiveListing(string listing, [NotNullWhen(true)] out BackupHeader? header)
    {
        header = null;
        string? databaseName = null;
        DateTime? takenAt = null;
        foreach (var rawLine in listing.Split('\n'))
        {
            var line = rawLine.TrimStart(';').Trim();
            if (line.StartsWith(ArchiveCreatedAt, StringComparison.Ordinal)
                && line.Length >= ArchiveCreatedAt.Length + ArchiveTimeFormat.Length
                && DateTime.TryParseExact(
                    line.AsSpan(ArchiveCreatedAt.Length, ArchiveTimeFormat.Length),
                    ArchiveTimeFormat,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var created))
            {
                takenAt = created;
            }
            else if (line.StartsWith(DatabaseNameLabel, StringComparison.Ordinal))
            {
                databaseName = line[DatabaseNameLabel.Length..].Trim();
            }
        }

        if (databaseName is null || takenAt is null)
        {
            return false;
        }

        header = new BackupHeader(databaseName, takenAt.Value);
        return true;
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
