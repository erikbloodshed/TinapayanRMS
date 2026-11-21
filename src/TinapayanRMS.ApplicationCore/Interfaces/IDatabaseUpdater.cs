namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// Brings the app's database up to this version of the app as it opens, so updating the app (its MSI, or the install
/// script) is all an update takes, on this PC or another: no source folder, no dotnet ef. A restore the app ended in the
/// middle of is put back first (IDatabaseBackups.RecoverInterruptedRestoreAsync), or an update would make a new, empty
/// database where it should be. Then a database that isn't there yet is made; one behind gets a backup first (BackupKind.BeforeUpdate), then the updates (migrations) it lacks; one a newer
/// copy has updated, or that isn't the app's, is refused with nothing changed.
/// </summary>
public interface IDatabaseUpdater
{
    /// <param name="applying">Told how many updates are to be applied, before the backup, when there are any: the time an update takes.</param>
    Task<DatabaseUpdate> UpdateAsync(Action<int>? applying, CancellationToken cancellationToken);
}

/// <summary>
/// What bringing the database up to date did: how many updates it applied (migrations, and data upgrades the app gained
/// after their migration ran), the backup taken first (none when nothing was applied or the database was new or empty),
/// and whether the database had to be made.
/// </summary>
/// <param name="RecoveredRestore">Whether a restore cut short was put back first.</param>
public sealed record DatabaseUpdate(int Applied, DatabaseBackup? Backup, bool Created, bool RecoveredRestore = false);
