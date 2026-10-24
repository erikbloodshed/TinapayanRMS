using Microsoft.EntityFrameworkCore;
using Npgsql;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.Tests.Common.Fixtures;

/// <summary>
/// A database of a test's own, on the same server as its project's test database, migrated and dropped again when the
/// test is done: for a test that backs up and restores a whole database, which mustn't be the shared one Respawn resets
/// for every other test (a restore puts everyone out of it), nor the developer's.
/// </summary>
public sealed class ThrowawayDatabase : IAsyncDisposable
{
    private readonly string _maintenanceConnectionString;

    private ThrowawayDatabase(string connectionString, string maintenanceConnectionString)
    {
        ConnectionString = connectionString;
        _maintenanceConnectionString = maintenanceConnectionString;
    }

    public string ConnectionString { get; }

    public string Name => new NpgsqlConnectionStringBuilder(ConnectionString).Database!;

    /// <summary>A new, migrated database named tinapayanrms_test_backup_ and eight characters of a GUID.</summary>
    /// <param name="like">A connection string to the server to make it on, such as the fixture's.</param>
    public static async Task<ThrowawayDatabase> CreateAsync(string like, CancellationToken cancellationToken)
    {
        var builder = new NpgsqlConnectionStringBuilder(like) { Database = $"tinapayanrms_test_backup_{Guid.NewGuid():N}"[..33] };
        // Dropping a database checkpoints first on Windows, which after a busy test run can outlast the default 30 seconds.
        var maintenance = new NpgsqlConnectionStringBuilder(like) { Database = "postgres", Pooling = false, CommandTimeout = 600 };
        var database = new ThrowawayDatabase(builder.ConnectionString, maintenance.ConnectionString);

        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync(cancellationToken);
        return database;
    }

    public TinapayanRMSDbContext CreateDbContext() => new(TinapayanRMSDbContext.CreateOptions(ConnectionString));

    public IDbContextFactory<TinapayanRMSDbContext> CreateDbContextFactory() => new Factory(this);

    /// <summary>
    /// Ends this account's connections to the database, then drops it. Not WITH (FORCE), which also tries to end an
    /// autovacuum worker in it, and an account that isn't a superuser may not.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(_maintenanceConnectionString);
        await connection.OpenAsync();
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var disconnect = new NpgsqlCommand(
                "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity "
                + "WHERE datname = @name AND pid <> pg_backend_pid() AND backend_type = 'client backend' AND usename = current_user",
                connection);
            disconnect.Parameters.AddWithValue("name", Name);
            if ((long)(await disconnect.ExecuteScalarAsync())! == 0)
            {
                break;
            }

            await Task.Delay(100);
        }

        await using var command = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{Name}\"", connection);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Factory(ThrowawayDatabase database) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => database.CreateDbContext();
    }
}
