using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Respawn;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.Tests.Common.Fixtures;

/// <summary>
/// Integration tests run against a test database of their project's own (never the
/// developer's real "TinapayanRMS" one), migrated once per test run and reset between
/// tests with Respawn. Each test project that uses one names it in its own DatabaseFixture,
/// since `dotnet test --solution` runs the test projects side by side and a shared database
/// would be reset under another project's running test. No table carries migration-seeded
/// baseline rows anymore (UnitOfMeasure is fully user-managed, with no protected/well-known
/// ids), so every table -- UnitsOfMeasure included -- gets the same full Respawn reset.
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

        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString("Default"))
        {
            InitialCatalog = databaseName,
        };

        ConnectionString = builder.ConnectionString;
    }

    public async ValueTask InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<TinapayanRMSDbContext>()
            .UseSqlServer(ConnectionString)
            .Options;

        await using (var dbContext = new TinapayanRMSDbContext(options))
        {
            await dbContext.Database.MigrateAsync();

            // Express turns AUTO_CLOSE on for a database it creates, which shuts it down when idle and makes the
            // next run's first reads slow. Set here so a test database dropped and recreated gets it too.
            await dbContext.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET AUTO_CLOSE OFF");
        }

        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();

        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.SqlServer,
            TablesToIgnore = [.. TablesExcludedFromReset.Select(t => new Respawn.Graph.Table(t))],
        });
    }

    public async Task ResetAsync()
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
