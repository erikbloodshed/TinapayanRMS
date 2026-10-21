using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Respawn;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.Tests.Common.Fixtures;

/// <summary>
/// Integration tests run against a test database of their project's own (never the
/// developer's real "tinapayanrms" one), migrated once per test run and reset between
/// tests with Respawn. Each test project that uses one names it in its own DatabaseFixture,
/// since `dotnet test --solution` runs the test projects side by side and a shared database
/// would be reset under another project's running test. No table carries migration-seeded
/// baseline rows the tests rely on (UnitOfMeasure is fully user-managed, with no protected/well-known
/// ids), so every table -- units_of_measure included -- gets the same full Respawn reset.
/// </summary>
public abstract class DatabaseFixtureBase : IAsyncLifetime
{
    private static readonly string[] TablesExcludedFromReset = ["__EFMigrationsHistory"];

    private Respawner _respawner = null!;

    public string ConnectionString { get; }

    protected DatabaseFixtureBase(string databaseName)
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var builder = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Default"))
        {
            Database = databaseName,
        };

        ConnectionString = builder.ConnectionString;
    }

    public async ValueTask InitializeAsync()
    {
        await using (var dbContext = new TinapayanRMSDbContext(TinapayanRMSDbContext.CreateOptions(ConnectionString)))
        {
            await dbContext.Database.MigrateAsync();
        }

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [.. TablesExcludedFromReset.Select(t => new Respawn.Graph.Table(t))],
        });
    }

    /// <summary>
    /// Empties every table. Respawn truncates them, which waits for the locks any read still running holds, such as a
    /// background load the last test left going; PostgreSQL ends one of the two as a deadlock when they cross, so it's
    /// tried again.
    /// </summary>
    public async Task ResetAsync()
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var connection = new NpgsqlConnection(ConnectionString);
                await connection.OpenAsync();
                await _respawner.ResetAsync(connection);
                return;
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.DeadlockDetected && attempt < 5)
            {
                await Task.Delay(100 * attempt);
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
