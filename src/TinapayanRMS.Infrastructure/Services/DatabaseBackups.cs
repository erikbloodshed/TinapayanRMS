using Microsoft.EntityFrameworkCore;
using Npgsql;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The database's backups, as PostgreSQL's own (2026-10-21, replacing SQL Server's BACKUP and RESTORE): pg_dump writes the
/// whole database to a file in <see cref="Folder"/> in its custom format, and pg_restore reads one back. A restore goes into
/// a scratch database first, which is checked and brought up to date before it takes the database's place, so a backup
/// that's refused or fails part-way never touches the data. Every statement runs on its own unpooled connection to the
/// server's maintenance database (postgres), never the app's pooled ones.
/// </summary>
/// <param name="folder">Where the backups go: <see cref="DefaultFolder"/>, or a test's own folder.</param>
/// <param name="toolsFolder">The folder holding pg_dump and pg_restore (Backups:ToolsFolder), or null to look for them.</param>
/// <param name="toolTimeout">How long pg_dump or pg_restore may run before it's stopped; half an hour unless a test says otherwise.</param>
public sealed class DatabaseBackups(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory, string folder, string? toolsFolder = null, TimeSpan? toolTimeout = null)
    : IDatabaseBackups, IDisposable
{
    /// <summary>%ProgramData%\TinapayanRMS\Backups: one folder for everyone using the app on this computer, as the database is.</summary>
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Backups");

    /// <summary>The maintenance database every PostgreSQL server has, which the statements that create, rename and drop a database run in.</summary>
    private const string MaintenanceDatabase = "postgres";

    /// <summary>How long a restore waits for the database's other connections to close once they've been told to.</summary>
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Ten minutes for a statement on the maintenance database. On Windows, PostgreSQL checkpoints before it drops a
    /// database, syncing every file it has written since the last one: after a busy spell that took 30 seconds on the
    /// development PC (found by TinapayanRMS_Web, 2026-10-11), Npgsql's default timeout.
    /// </summary>
    private const int MaintenanceCommandTimeoutSeconds = 600;

    /// <summary>
    /// How old a partial file must be before it's taken for one left behind (a computer turned off mid-way) and deleted.
    /// Far longer than a tool may run, so it's never one a backup in another copy of the app is still writing.
    /// </summary>
    private static readonly TimeSpan PartialAbandonedAfter = TimeSpan.FromDays(1);

    private readonly PostgresTools _tools = new(toolsFolder, toolTimeout);

    /// <summary>
    /// The day's automatic backup starts in the background at startup, while Settings may already be taking one or
    /// restoring, so backups and restores take turns.
    /// </summary>
    private readonly SemaphoreSlim _turn = new(1, 1);

    public string Folder => folder;

    public IReadOnlyList<DatabaseBackup> List() =>
        Directory.Exists(folder)
            ? [.. new DirectoryInfo(folder).EnumerateFiles("*" + BackupRules.Extension).Select(Describe).OrderByDescending(b => b.TakenAt)]
            : [];

    public async Task<DatabaseBackup?> NewestAsync(CancellationToken cancellationToken) =>
        BackupRules.Newest(List(), (await TargetAsync(cancellationToken)).Database);

    public async Task<DatabaseBackup> BackUpAsync(BackupKind kind, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(cancellationToken);
        await _turn.WaitAsync(cancellationToken);
        try
        {
            return await BackUpInTurnAsync(target, kind, DateTime.Now, cancellationToken);
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task<DatabaseBackup?> BackUpDailyIfDueAsync(DateTime now, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(cancellationToken);
        await _turn.WaitAsync(cancellationToken);
        try
        {
            return BackupRules.IsDailyBackupDue(List(), target.Database, now)
                ? await BackUpInTurnAsync(target, BackupKind.Automatic, now, cancellationToken)
                : null;
        }
        finally
        {
            _turn.Release();
        }
    }

    public async Task<DatabaseBackup> CopyInAsync(string sourcePath, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(cancellationToken);
        PrepareFolder();
        var path = FreePath(target.Database, BackupKind.CopiedIn, DateTime.Now);
        await CopyAsync(sourcePath, path, cancellationToken);
        return Describe(new FileInfo(path));
    }

    public Task CopyToAsync(DatabaseBackup backup, string destinationPath, CancellationToken cancellationToken) =>
        string.Equals(Path.GetFullPath(destinationPath), Path.GetFullPath(backup.Path), StringComparison.OrdinalIgnoreCase)
            ? Task.CompletedTask   // onto itself: opening it to write would empty it before it was read
            : CopyAsync(backup.Path, destinationPath, cancellationToken);

    public async Task<BackupHeader> InspectAsync(DatabaseBackup backup, CancellationToken cancellationToken) =>
        await ReadHeaderAsync(await TargetAsync(cancellationToken), backup.Path, cancellationToken);

    public async Task<RestoredBackup> RestoreAsync(DatabaseBackup backup, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(cancellationToken);
        await _turn.WaitAsync(cancellationToken);
        try
        {
            // A file that isn't a backup is refused before the safety backup is even taken.
            await ReadHeaderAsync(target, backup.Path, cancellationToken);
            var safety = await BackUpInTurnAsync(target, BackupKind.BeforeRestore, DateTime.Now, cancellationToken);
            var scratch = target.WithDatabase(target.Database + "_restoring");
            try
            {
                await CreateDatabaseAsync(target, scratch.Database, cancellationToken);
                await RestoreFileAsync(scratch, backup.Path, cancellationToken);
                var updatesApplied = await BringUpToDateAsync(scratch, cancellationToken);
                await SwapInAsync(target, scratch.Database, cancellationToken);
                return new RestoredBackup(safety, updatesApplied);
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();
                await DropDatabaseAsync(target, scratch.Database, CancellationToken.None);
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    public void Delete(DatabaseBackup backup) => File.Delete(backup.Path);

    public void Dispose() => _turn.Dispose();

    /// <summary>
    /// Where a new backup goes: a name no file in the folder has yet, whole or still being written
    /// (BackupRules.FreeFileNameFor), so it overwrites none.
    /// </summary>
    private string FreePath(string databaseName, BackupKind kind, DateTime takenAt) =>
        Path.Combine(folder, BackupRules.FreeFileNameFor(databaseName, kind, takenAt, name =>
            File.Exists(Path.Combine(folder, name)) || File.Exists(Path.Combine(folder, name + BackupRules.PartialSuffix))));

    /// <summary>Creates the folder if it's missing, open to every Windows user of the computer (BackupFolder).</summary>
    private void PrepareFolder()
    {
        if (OperatingSystem.IsWindows())
        {
            BackupFolder.Ensure(folder);
        }
        else
        {
            Directory.CreateDirectory(folder);
        }
    }

    /// <summary>A file in the folder, by its name when the app named it, else as copied in, dated by when it was written.</summary>
    private static DatabaseBackup Describe(FileInfo file) =>
        BackupRules.TryParse(file.Name, out var databaseName, out var kind, out var takenAt)
            ? new(file.FullName, databaseName, kind, takenAt, file.Length)
            : new(file.FullName, null, BackupKind.CopiedIn, file.LastWriteTime, file.Length);

    /// <summary>
    /// pg_dump writes to a partial file, which only takes the backup's name once pg_dump has finished without an error.
    /// So whatever stops it part-way (a power cut, the process killed) leaves nothing that reads as a backup. pg_dump makes
    /// its file before it even connects, and under the backup's own name a stray one counted as the day's backup, so
    /// none was taken that day (found 2026-11-03, by a code review).
    /// </summary>
    private async Task<DatabaseBackup> BackUpInTurnAsync(Target target, BackupKind kind, DateTime takenAt, CancellationToken cancellationToken)
    {
        PrepareFolder();
        var path = FreePath(target.Database, kind, takenAt);
        var partial = path + BackupRules.PartialSuffix;
        try
        {
            await _tools.RunAsync("pg_dump", target.Connection, ["--format=custom", "--file", partial, "--dbname", target.Database], cancellationToken);
            File.Move(partial, path);
        }
        catch
        {
            DeleteIfPossible(partial);
            throw;
        }

        DeleteAbandonedPartials();
        if (kind == BackupKind.Automatic)
        {
            DeleteOldAutomaticBackups(target.Database);
        }

        return Describe(new FileInfo(path));
    }

    /// <summary>
    /// The automatic backups beyond the ones kept. One that can't be deleted now (open in another program) is left for the
    /// next day's backup to try again: the backup just taken is what matters.
    /// </summary>
    private void DeleteOldAutomaticBackups(string databaseName)
    {
        foreach (var old in BackupRules.AutomaticToDelete(List(), databaseName))
        {
            DeleteIfPossible(old.Path);
        }
    }

    /// <summary>Partial files a run that never finished left behind: nothing will ever make them whole.</summary>
    private void DeleteAbandonedPartials()
    {
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*" + BackupRules.PartialSuffix))
        {
            if (DateTime.UtcNow - file.LastWriteTimeUtc > PartialAbandonedAfter)
            {
                DeleteIfPossible(file.FullName);
            }
        }
    }

    /// <summary>Deletes a file that's in the way but isn't what matters: one that can't be deleted now is tried again later, and hides no other failure.</summary>
    private static void DeleteIfPossible(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Open in another program, or a tool that's stopped but hasn't let go of it yet.
        }
    }

    /// <summary>
    /// What the file says of itself, as pg_restore lists it. A file pg_restore can't read is refused as no backup, and one
    /// from a newer PostgreSQL than this computer's as that.
    /// </summary>
    private async Task<BackupHeader> ReadHeaderAsync(Target target, string path, CancellationToken cancellationToken)
    {
        string listing;
        try
        {
            listing = await _tools.RunAsync("pg_restore", target.Connection, ["--list", path], cancellationToken);
        }
        catch (PostgresToolException ex) when (ex.Error.Contains("unsupported version", StringComparison.OrdinalIgnoreCase))
        {
            throw new BusinessRuleException(BackupRules.NewerPostgresText, ex);
        }
        catch (PostgresToolException ex)
        {
            throw new BusinessRuleException(BackupRules.NotADatabaseBackupText, ex);
        }

        return BackupRules.TryReadArchiveListing(listing, out var header)
            ? header
            : throw new BusinessRuleException(BackupRules.NotADatabaseBackupText);
    }

    /// <summary>
    /// Restores <paramref name="path"/> into the empty scratch database, all or nothing. The objects are made the restoring
    /// account's own, whoever owned them where the backup was taken, as on another computer.
    /// </summary>
    private async Task RestoreFileAsync(Target scratch, string path, CancellationToken cancellationToken) =>
        await _tools.RunAsync(
            "pg_restore",
            scratch.Connection,
            ["--no-owner", "--no-privileges", "--exit-on-error", "--single-transaction", "--dbname", scratch.Database, path],
            cancellationToken);

    /// <summary>
    /// Brings the restored backup up to this version of the app, as the user chose (2026-10-03): the updates it lacks are
    /// applied, so the app works with it straight away. One from a later version, or of another app's database, is refused
    /// (BackupRules.UpdatesNeeded) before it takes the database's place.
    /// </summary>
    private static async Task<int> BringUpToDateAsync(Target scratch, CancellationToken cancellationToken)
    {
        await using var dbContext = new TinapayanRMSDbContext(TinapayanRMSDbContext.CreateOptions(scratch.Connection.ConnectionString));
        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var needed = BackupRules.UpdatesNeeded(applied, [.. dbContext.Database.GetMigrations()]);
        if (needed.Count > 0)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        return needed.Count;
    }

    /// <summary>
    /// Puts the restored scratch database in the database's place by renaming the two, and drops the one it replaced.
    /// Everyone is put out of the database first (the app's own pooled connections included, which the app reloads after
    /// anyway) and kept out while the names change. If either rename fails, the database gets its name back and is opened
    /// to everyone again, as it was.
    /// </summary>
    private static async Task SwapInAsync(Target target, string scratchDatabase, CancellationToken cancellationToken)
    {
        var replaced = target.Database + "_replaced";
        await DropDatabaseAsync(target, replaced, cancellationToken);
        NpgsqlConnection.ClearAllPools();
        await using var connection = await OpenMaintenanceAsync(target, cancellationToken);
        await ExecuteAsync(connection, $"ALTER DATABASE {Quote(target.Database)} WITH ALLOW_CONNECTIONS false", cancellationToken);
        try
        {
            await DisconnectEveryoneAsync(connection, target.Database, cancellationToken);
            await ExecuteAsync(connection, $"ALTER DATABASE {Quote(target.Database)} RENAME TO {Quote(replaced)}", cancellationToken);
        }
        catch
        {
            await ExecuteAsync(connection, $"ALTER DATABASE {Quote(target.Database)} WITH ALLOW_CONNECTIONS true", CancellationToken.None);
            throw;
        }

        try
        {
            await DisconnectEveryoneAsync(connection, scratchDatabase, cancellationToken);
            await ExecuteAsync(connection, $"ALTER DATABASE {Quote(scratchDatabase)} RENAME TO {Quote(target.Database)}", cancellationToken);
        }
        catch
        {
            await ExecuteAsync(connection, $"ALTER DATABASE {Quote(replaced)} RENAME TO {Quote(target.Database)}", CancellationToken.None);
            await ExecuteAsync(connection, $"ALTER DATABASE {Quote(target.Database)} WITH ALLOW_CONNECTIONS true", CancellationToken.None);
            throw;
        }

        // It's closed to new connections, and everyone was put out of it before the rename.
        await ExecuteAsync(connection, $"DROP DATABASE {Quote(replaced)}", CancellationToken.None);
    }

    /// <summary>
    /// Ends every other connection to <paramref name="database"/> and waits until they've gone: a database can't be renamed
    /// while anyone is in it. Only client connections count: the server's own workers, such as autovacuum, aren't the
    /// app's to end, and the rename stops them itself. A connection another account holds can't be ended from here, and
    /// is waited for.
    /// </summary>
    private static async Task DisconnectEveryoneAsync(NpgsqlConnection connection, string database, CancellationToken cancellationToken)
    {
        var giveUpAt = DateTime.UtcNow + DisconnectTimeout;
        while (true)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT count(*), count(pg_terminate_backend(pid)) FILTER (WHERE usename = current_user)
                FROM pg_stat_activity
                WHERE datname = @database AND pid <> pg_backend_pid() AND backend_type = 'client backend'
                """,
                connection);
            command.Parameters.AddWithValue("database", database);
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                if (reader.GetInt64(0) == 0)
                {
                    return;
                }
            }

            if (DateTime.UtcNow > giveUpAt)
            {
                throw new BusinessRuleException("Another program kept using the database, so it couldn't be restored. Close it, then try again.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken);
        }
    }

    private static async Task CreateDatabaseAsync(Target target, string database, CancellationToken cancellationToken)
    {
        await DropDatabaseAsync(target, database, cancellationToken);
        await using var connection = await OpenMaintenanceAsync(target, cancellationToken);
        await ExecuteAsync(connection, $"CREATE DATABASE {Quote(database)} TEMPLATE template0 ENCODING 'UTF8'", cancellationToken);
    }

    /// <summary>
    /// Drops <paramref name="database"/> once everyone is out of it. Not WITH (FORCE): that ends the server's own workers
    /// in it too, and an account that isn't a superuser may not end an autovacuum worker, which a database just filled by a
    /// restore often has (found by TinapayanRMS_Web, 2026-10-11). A plain DROP DATABASE stops those itself.
    /// </summary>
    private static async Task DropDatabaseAsync(Target target, string database, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMaintenanceAsync(target, cancellationToken);
        await DisconnectEveryoneAsync(connection, database, cancellationToken);
        await ExecuteAsync(connection, $"DROP DATABASE IF EXISTS {Quote(database)}", cancellationToken);
    }

    /// <summary>
    /// Copies to a partial file beside <paramref name="to"/>, written out to the disk and only then given its name, replacing
    /// a file there. A USB stick pulled out part-way leaves nothing that reads as a backup, and the file it would have
    /// replaced as it was.
    /// </summary>
    private static async Task CopyAsync(string from, string to, CancellationToken cancellationToken)
    {
        var partial = to + BackupRules.PartialSuffix;
        try
        {
            await using (var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var destination = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await source.CopyToAsync(destination, cancellationToken);
                destination.Flush(flushToDisk: true);
            }

            File.Move(partial, to, overwrite: true);
        }
        catch
        {
            DeleteIfPossible(partial);
            throw;
        }
    }

    /// <summary>The database the app uses, and how to reach its server, from the app's own connection string.</summary>
    private async Task<Target> TargetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = new NpgsqlConnectionStringBuilder(dbContext.Database.GetConnectionString());
        return new Target(connection);
    }

    private static async Task<NpgsqlConnection> OpenMaintenanceAsync(Target target, CancellationToken cancellationToken)
    {
        var settings = new NpgsqlConnectionStringBuilder(target.WithDatabase(MaintenanceDatabase).Connection.ConnectionString)
        {
            CommandTimeout = MaintenanceCommandTimeoutSeconds,
        };
        var connection = new NpgsqlConnection(settings.ConnectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A database's name as an identifier: a name can't be a parameter where a statement names a database.</summary>
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>A database and how to reach its server, unpooled: a pooled connection would keep it in use after a statement.</summary>
    private sealed class Target
    {
        public Target(NpgsqlConnectionStringBuilder connection)
        {
            Connection = new NpgsqlConnectionStringBuilder(connection.ConnectionString) { Pooling = false };
        }

        public NpgsqlConnectionStringBuilder Connection { get; }

        public string Database => Connection.Database!;

        public Target WithDatabase(string database) => new(new NpgsqlConnectionStringBuilder(Connection.ConnectionString) { Database = database });
    }
}
