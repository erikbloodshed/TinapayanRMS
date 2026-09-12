using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Respawn;
using TinapayanRMS.Domain;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.Tests.Infrastructure;

/// <summary>
/// Integration tests run against a dedicated "TinapayanRMS_Test" database (never
/// the developer's real "TinapayanRMS" one), migrated once per test run and reset
/// between tests with Respawn. UnitsOfMeasure is excluded from Respawn's own reset
/// (its migration-seeded rows, ids 1-6, are baseline data every other test's FK
/// references may depend on, and Respawn has no notion of "reset to this seed
/// state" -- only "delete everything" or "touch nothing"). Any *test-added* row in
/// that table (id > 6, from a UnitOfMeasureRepository AddAsync/DeleteAsync test) is
/// still cleared after every test by the explicit delete in ResetAsync below, so
/// UnitsOfMeasure keeps the same test-to-test isolation every other table gets.
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

        await using var command = connection.CreateCommand();
        command.CommandText = $"DELETE FROM UnitsOfMeasure WHERE Id > {WellKnownUnitOfMeasureIds.Piece};";
        await command.ExecuteNonQueryAsync();
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
