using Npgsql;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// The databases a run measures on: the golden master's anonymized copy of the shop's data (Golden/costing-fixture.backup),
/// restored as a backup is restored (Copy In, then Restore, which brings it up to this build's migrations), and a copy of
/// it for the scenarios that write, so a write never moves what the reads read. Named tinapayanrms_bench_ (BenchServer),
/// and dropped again as the run ends; the clean command drops what a killed run left (<see cref="LeftoversAsync"/>).
/// </summary>
public sealed class BenchDatabases : IAsyncDisposable
{
    private readonly ThrowawayDatabase _read;
    private readonly ThrowawayDatabase _write;

    private BenchDatabases(ThrowawayDatabase read, ThrowawayDatabase write)
    {
        _read = read;
        _write = write;
    }

    /// <summary>
    /// The restored copy, which the reads, the screens and the start run on, opened as the app opens its database: a
    /// read-only session would refuse a write the app may make as a screen opens, and pools differently.
    /// </summary>
    public string Read => _read.ConnectionString;

    /// <summary>Its copy, which the cost cascade's saves write to.</summary>
    public string Write => _write.ConnectionString;

    /// <summary>The fixture restored, and its copy, named tinapayanrms_bench_read_ and _write_ with the same eight characters.</summary>
    public static async Task<BenchDatabases> CreateAsync(CancellationToken cancellationToken)
    {
        var run = Guid.NewGuid().ToString("N")[..8];
        var read = await RestoreAsync($"{BenchServer.Prefix}read_{run}", cancellationToken);
        try
        {
            return new BenchDatabases(read, await CopyAsync(read, $"{BenchServer.Prefix}write_{run}", cancellationToken));
        }
        catch
        {
            await read.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// The fixture restored into a new database <paramref name="name"/>, brought up to this build's migrations, as the
    /// golden master restores it (CostingGoldenMasterTests): an older copy is updated by every migration since it was taken.
    /// </summary>
    public static async Task<ThrowawayDatabase> RestoreAsync(string name, CancellationToken cancellationToken)
    {
        BenchServer.Ensure(name);
        var database = await ThrowawayDatabase.CreateAsync(BenchServer.Maintenance, cancellationToken, name);
        var folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "Test Backups", Guid.NewGuid().ToString("N"));
        try
        {
            using (var backups = new DatabaseBackups(database.CreateDbContextFactory(), folder))
            {
                var copied = await backups.CopyInAsync(Path.Combine(AppContext.BaseDirectory, "Golden", "costing-fixture.backup"), cancellationToken);
                await backups.RestoreAsync(copied, cancellationToken);
            }

            await SettleAsync(database.ConnectionString, cancellationToken);
            return database;
        }
        catch
        {
            await database.DisposeAsync();
            throw;
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    /// <summary>The restore's own backup folder gone, if it can be: a file still held (a scanner's) mustn't fail a restore that worked.</summary>
    private static void DeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>Drops <paramref name="name"/>, one of the benchmarks' databases, if it's there.</summary>
    public static ValueTask DropAsync(string name)
    {
        BenchServer.Ensure(name);
        return ThrowawayDatabase.Of(BenchServer.Maintenance, name).DisposeAsync();
    }

    /// <summary>
    /// The databases a run made that are still there and nobody is connected to: a run killed before it dropped them. One
    /// with a connection open belongs to a run still going (a budget test run, a comparison in another terminal).
    /// </summary>
    public static async Task<IReadOnlyList<string>> LeftoversAsync(CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(BenchServer.Maintenance);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            "SELECT d.datname FROM pg_database d WHERE starts_with(d.datname, @prefix) "
            + "AND NOT EXISTS (SELECT 1 FROM pg_stat_activity a WHERE a.datname = d.datname) ORDER BY d.datname",
            connection);
        command.Parameters.AddWithValue("prefix", BenchServer.Prefix);
        List<string> names = [];
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public async ValueTask DisposeAsync()
    {
        await _write.DisposeAsync();
        await _read.DisposeAsync();
    }

    /// <summary>
    /// <paramref name="connectionString"/>'s database vacuumed and analyzed. A database just restored has no planner
    /// statistics, so autovacuum analyzes every table soon after, at a moment that differs from run to run: meanwhile it
    /// loads the PC and the plans change under the run. An unchanged tree timed against itself came out a third slower in
    /// one of its reads, every round, until the statistics were made before anything was timed. A copy made from the
    /// database afterwards (<see cref="CopyAsync"/>) has them too.
    /// </summary>
    private static async Task SettleAsync(string connectionString, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Pooling = false, CommandTimeout = 600 };
        await using var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("VACUUM (ANALYZE)", connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// A copy of <paramref name="source"/> (CREATE DATABASE … TEMPLATE), far quicker than restoring it again: no connection
    /// may be open to it meanwhile, so the pools are cleared first.
    /// </summary>
    private static async Task<ThrowawayDatabase> CopyAsync(ThrowawayDatabase source, string name, CancellationToken cancellationToken)
    {
        BenchServer.Ensure(name);
        NpgsqlConnection.ClearAllPools();
        var maintenance = new NpgsqlConnectionStringBuilder(BenchServer.Maintenance) { Pooling = false, CommandTimeout = 600 };
        await using (var connection = new NpgsqlConnection(maintenance.ConnectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand($"CREATE DATABASE \"{name}\" TEMPLATE \"{source.Name}\"", connection);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return ThrowawayDatabase.Of(BenchServer.Maintenance, name);
    }
}
