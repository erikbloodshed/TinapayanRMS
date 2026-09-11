using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Respawn;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.Tests.Infrastructure;

/// <summary>
/// Integration tests run against a dedicated "TinapayanRMS_Test" database (never
/// the developer's real "TinapayanRMS" one), migrated once per test run and reset
/// between tests with Respawn. Seed data (currently just UnitsOfMeasure) is
/// excluded from the reset, since it's owned by migrations, not by tests.
/// </summary>
public class DatabaseFixture : IAsyncLifetime
{
    private static readonly string[] TablesExcludedFromReset = ["UnitsOfMeasure", "__EFMigrationsHistory"];

    private Respawner _respawner = null!;

    public string ConnectionString { get; }

    public DatabaseFixture()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .Build();

        var builder = new SqlConnectionStringBuilder(configuration.GetConnectionString("Default"))
        {
            InitialCatalog = "TinapayanRMS_Test",
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

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
