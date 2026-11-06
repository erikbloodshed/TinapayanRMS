using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Respawn;
using TUnit.Core.Interfaces;
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
/// <para>
/// TUnit makes one for each key a class's keyed ClassDataSource names (its DatabaseCollection), so a project with several
/// database collections has one of these for each, and each takes a database of its own: the first the project's name,
/// the next ones that name with _2, _3 and so on. The collections then run side by side, where one database would make
/// every database test of a project run after the other. TUnit initializes it before its first test.
/// </para>
/// </summary>
public abstract class DatabaseFixtureBase : IAsyncInitializer, IAsyncDisposable
{
    private static readonly string[] TablesExcludedFromReset = ["__EFMigrationsHistory"];

    /// <summary>How many fixtures of this run have taken each project's database name.</summary>
    private static readonly ConcurrentDictionary<string, int> FixturesByName = new();

    private Respawner _respawner = null!;

    public string ConnectionString { get; }

    protected DatabaseFixtureBase(string databaseName)
    {
        var number = FixturesByName.AddOrUpdate(databaseName, 1, (_, taken) => taken + 1);
        if (number > 1)
        {
            databaseName = $"{databaseName}_{number}";
        }

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

    public async Task InitializeAsync()
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
