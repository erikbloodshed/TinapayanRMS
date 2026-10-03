using System.Globalization;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// The database's backups, as SQL Server's own full backups (2026-10-03): BACKUP DATABASE … COPY_ONLY to a file in
/// <see cref="Folder"/>, which BackupFolder sets up for SQL Server's account to write to, and RESTORE DATABASE … REPLACE
/// from one. COPY_ONLY so a backup taken here never changes what a backup taken by other means would hold, and CHECKSUM
/// so a damaged file is caught when it's verified. Everything runs on its own connection to master, never the app's
/// pooled ones.
/// </summary>
/// <param name="folder">Where the backups go: <see cref="DefaultFolder"/>, or a test's own folder.</param>
public sealed class DatabaseBackups(IDbContextFactory<TinapayanRMSDbContext> dbContextFactory, string folder) : IDatabaseBackups, IDisposable
{
    /// <summary>%ProgramData%\TinapayanRMS\Backups: one folder for everyone using the app on this computer, as the database is.</summary>
    public static string DefaultFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Backups");

    /// <summary>Ten minutes: SqlClient's default 30 seconds is too short for a large database's backup or restore.</summary>
    private const int CommandTimeoutSeconds = 600;

    /// <summary>
    /// SQL Server takes one backup of a database at a time and fails a second with error 3023. The day's automatic backup
    /// starts in the background at startup, while Settings may already be taking one, so backups and restores take turns.
    /// </summary>
    private readonly SemaphoreSlim _turn = new(1, 1);

    public string Folder => folder;

    public IReadOnlyList<DatabaseBackup> List() =>
        Directory.Exists(folder)
            ? [.. new DirectoryInfo(folder).EnumerateFiles("*" + BackupRules.Extension).Select(Describe).OrderByDescending(b => b.TakenAt)]
            : [];

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
        await PrepareFolderAsync(target, cancellationToken);
        var path = Path.Combine(folder, BackupRules.FileNameFor(target.Database, BackupKind.CopiedIn, DateTime.Now));
        await CopyAsync(sourcePath, path, cancellationToken);
        return Describe(new FileInfo(path));
    }

    public Task CopyToAsync(DatabaseBackup backup, string destinationPath, CancellationToken cancellationToken) =>
        string.Equals(Path.GetFullPath(destinationPath), Path.GetFullPath(backup.Path), StringComparison.OrdinalIgnoreCase)
            ? Task.CompletedTask   // onto itself: opening it to write would empty it before it was read
            : CopyAsync(backup.Path, destinationPath, cancellationToken);

    public async Task<BackupHeader> InspectAsync(DatabaseBackup backup, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(cancellationToken);
        await using var connection = await OpenMasterAsync(target, cancellationToken);
        return (await ReadHeaderAsync(connection, backup.Path, cancellationToken)).Header;
    }

    public async Task<RestoredBackup> RestoreAsync(DatabaseBackup backup, CancellationToken cancellationToken)
    {
        var target = await TargetAsync(cancellationToken);
        await _turn.WaitAsync(cancellationToken);
        try
        {
            // Everything that can be checked without touching the database first: a file that isn't a whole database's
            // backup, or doesn't fit this one, is refused before the safety backup is even taken.
            var plan = await PlanRestoreAsync(target, backup.Path, cancellationToken);
            var safety = await BackUpInTurnAsync(target, BackupKind.BeforeRestore, DateTime.Now, cancellationToken);
            try
            {
                await RestoreFileAsync(target, plan, cancellationToken);
                return new RestoredBackup(safety, await BringUpToDateAsync(cancellationToken));
            }
            catch (Exception failure)
            {
                await PutBackAsync(target, safety, failure);
                throw;
            }
        }
        finally
        {
            _turn.Release();
        }
    }

    public void Delete(DatabaseBackup backup) => File.Delete(backup.Path);

    public void Dispose() => _turn.Dispose();

    /// <summary>A file in the folder, by its name when the app named it, else as copied in, dated by when it was written.</summary>
    private static DatabaseBackup Describe(FileInfo file) =>
        BackupRules.TryParse(file.Name, out var databaseName, out var kind, out var takenAt)
            ? new(file.FullName, databaseName, kind, takenAt, file.Length)
            : new(file.FullName, null, BackupKind.CopiedIn, file.LastWriteTime, file.Length);

    private async Task<DatabaseBackup> BackUpInTurnAsync(Target target, BackupKind kind, DateTime takenAt, CancellationToken cancellationToken)
    {
        await PrepareFolderAsync(target, cancellationToken);
        var path = Path.Combine(folder, BackupRules.FileNameFor(target.Database, kind, takenAt));
        await using (var connection = await OpenMasterAsync(target, cancellationToken))
        {
            await ExecuteAsync(
                connection,
                "BACKUP DATABASE @database TO DISK = @path WITH COPY_ONLY, CHECKSUM, INIT",
                cancellationToken,
                ("@database", target.Database),
                ("@path", path));
        }

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
            try
            {
                File.Delete(old.Path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Tried again after the next automatic backup.
            }
        }
    }

    /// <summary>Refuses a database on another computer, then makes sure SQL Server's account can write to the folder.</summary>
    private async Task PrepareFolderAsync(Target target, CancellationToken cancellationToken)
    {
        string machine;
        string? instance;
        await using (var connection = await OpenMasterAsync(target, cancellationToken))
        await using (var command = Command(
            connection,
            "SELECT CAST(SERVERPROPERTY('MachineName') AS nvarchar(128)), CAST(SERVERPROPERTY('InstanceName') AS nvarchar(128))"))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            await reader.ReadAsync(cancellationToken);
            machine = reader.GetString(0);
            instance = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        BackupRules.EnsureLocalServer(machine, Environment.MachineName);
        if (OperatingSystem.IsWindows())
        {
            BackupFolder.Ensure(folder, BackupRules.ServiceAccountFor(instance));
        }
        else
        {
            Directory.CreateDirectory(folder);
        }
    }

    /// <summary>
    /// What restoring <paramref name="path"/> takes, checked without changing anything: the last whole-database backup in
    /// the file, its two files moved onto this database's, and the backup verified as readable throughout.
    /// </summary>
    private static async Task<RestorePlan> PlanRestoreAsync(Target target, string path, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMasterAsync(target, cancellationToken);
        var (_, position, hasChecksums) = await ReadHeaderAsync(connection, path, cancellationToken);

        var backupFiles = new List<(string LogicalName, bool IsLog)>();
        await using (var command = Command(connection, "RESTORE FILELISTONLY FROM DISK = @path WITH FILE = @position", ("@path", path), ("@position", position)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                backupFiles.Add(((string)reader["LogicalName"], (string)reader["Type"] == "L"));
            }
        }

        var databaseFiles = new List<(string PhysicalPath, bool IsLog)>();
        await using (var command = Command(connection, "SELECT physical_name, type FROM sys.master_files WHERE database_id = DB_ID(@database)", ("@database", target.Database)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                databaseFiles.Add((reader.GetString(0), reader.GetByte(1) == 1));
            }
        }

        var moves = BackupRules.RestoreMoves(backupFiles, databaseFiles);
        await ExecuteAsync(
            connection,
            hasChecksums ? "RESTORE VERIFYONLY FROM DISK = @path WITH FILE = @position, CHECKSUM" : "RESTORE VERIFYONLY FROM DISK = @path WITH FILE = @position",
            cancellationToken,
            ("@path", path),
            ("@position", position));
        return new RestorePlan(path, position, moves);
    }

    /// <summary>
    /// The last whole-database backup in the file (RESTORE HEADERONLY lists every backup set it holds; BackupType 1 is a
    /// database backup, COPY_ONLY or not). A file with none, such as a log backup, is refused.
    /// </summary>
    private static async Task<(BackupHeader Header, int Position, bool HasChecksums)> ReadHeaderAsync(
        SqlConnection connection, string path, CancellationToken cancellationToken)
    {
        (BackupHeader, int, bool)? last = null;
        await using var command = Command(connection, "RESTORE HEADERONLY FROM DISK = @path", ("@path", path));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Convert.ToInt32(reader["BackupType"], CultureInfo.InvariantCulture) == 1)
            {
                last = (
                    new BackupHeader((string)reader["DatabaseName"], (DateTime)reader["BackupFinishDate"]),
                    Convert.ToInt32(reader["Position"], CultureInfo.InvariantCulture),
                    Convert.ToBoolean(reader["HasBackupChecksums"], CultureInfo.InvariantCulture));
            }
        }

        return last ?? throw new BusinessRuleException(BackupRules.NotADatabaseBackupText);
    }

    /// <summary>
    /// Restores over the database, on one connection: everyone else is put out of it first (the app's own pooled
    /// connections included, which the app restarts after anyway), and it's opened to everyone again whatever happens.
    /// </summary>
    private static async Task RestoreFileAsync(Target target, RestorePlan plan, CancellationToken cancellationToken)
    {
        SqlConnection.ClearAllPools();
        await using var connection = await OpenMasterAsync(target, cancellationToken);
        await SetDatabaseOptionAsync(connection, target, "SINGLE_USER WITH ROLLBACK IMMEDIATE", cancellationToken);
        try
        {
            await ExecuteAsync(
                connection,
                "RESTORE DATABASE @database FROM DISK = @path WITH FILE = @position, REPLACE, RECOVERY, "
                + "MOVE @dataName TO @dataPath, MOVE @logName TO @logPath",
                cancellationToken,
                ("@database", target.Database),
                ("@path", plan.Path),
                ("@position", plan.Position),
                ("@dataName", plan.Moves[0].LogicalName),
                ("@dataPath", plan.Moves[0].PhysicalPath),
                ("@logName", plan.Moves[1].LogicalName),
                ("@logPath", plan.Moves[1].PhysicalPath));
        }
        finally
        {
            try
            {
                await SetDatabaseOptionAsync(connection, target, "MULTI_USER", CancellationToken.None);
            }
            catch (SqlException)
            {
                // A restore that failed part-way can leave the database restoring, which takes no options; the safety
                // backup put back over it opens it again.
            }
        }

        // Express turns AUTO_CLOSE on for a database it creates, and a backup brings whatever its database had.
        await SetDatabaseOptionAsync(connection, target, "AUTO_CLOSE OFF", cancellationToken);
        SqlConnection.ClearAllPools();
    }

    /// <summary>
    /// Brings a restored backup up to this version of the app, as the user chose (2026-10-03): the updates it lacks are
    /// applied, so the app works with it straight away. One from a later version, or of another app's database, is
    /// refused (BackupRules.UpdatesNeeded), and the safety backup goes back.
    /// </summary>
    private async Task<int> BringUpToDateAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var applied = (await dbContext.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var needed = BackupRules.UpdatesNeeded(applied, [.. dbContext.Database.GetMigrations()]);
        if (needed.Count > 0)
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        return needed.Count;
    }

    /// <summary>Puts the safety backup back after a restore failed or was refused part-way, so the data is as it was.</summary>
    private static async Task PutBackAsync(Target target, DatabaseBackup safety, Exception failure)
    {
        try
        {
            await RestoreFileAsync(target, await PlanRestoreAsync(target, safety.Path, CancellationToken.None), CancellationToken.None);
        }
        catch (Exception putBackFailure)
        {
            throw new BusinessRuleException(
                "The restore failed, and so did putting the data from before it back. "
                + $"That data is safe in {safety.Path}: ask whoever looks after the app to restore it.",
                new AggregateException(failure, putBackFailure));
        }
    }

    private static async Task CopyAsync(string from, string to, CancellationToken cancellationToken)
    {
        // A new file, so it takes the folder's own permissions; File.Copy would carry the source's.
        await using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using var destination = new FileStream(to, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await source.CopyToAsync(destination, cancellationToken);
    }

    /// <summary>The database the app uses, and a connection string to the server's master database, from the app's own.</summary>
    private async Task<Target> TargetAsync(CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var connection = new SqlConnectionStringBuilder(dbContext.Database.GetConnectionString());
        var database = connection.InitialCatalog;
        connection.InitialCatalog = "master";
        connection.Pooling = false;
        return new Target(database, connection.ConnectionString);
    }

    private static async Task<SqlConnection> OpenMasterAsync(Target target, CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(target.MasterConnectionString);
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

    /// <summary>ALTER DATABASE … SET <paramref name="option"/>: the database's name quoted by the server (QUOTENAME), since it can't be a parameter there.</summary>
    private static Task SetDatabaseOptionAsync(SqlConnection connection, Target target, string option, CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            $"DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@database) + N' SET {option}'; EXEC (@sql);",
            cancellationToken,
            ("@database", target.Database));

    private static async Task ExecuteAsync(
        SqlConnection connection, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, sql, parameters);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqlCommand Command(SqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new SqlCommand(sql, connection) { CommandTimeout = CommandTimeoutSeconds };
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    private sealed record Target(string Database, string MasterConnectionString);

    private sealed record RestorePlan(string Path, int Position, IReadOnlyList<(string LogicalName, string PhysicalPath)> Moves);
}
