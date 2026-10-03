using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.Tests.Common.Fixtures;

/// <summary>
/// A database of a test's own, on the same server as its project's test database, migrated and dropped again when the
/// test is done: for a test that backs up and restores a whole database, which mustn't be the shared one Respawn resets
/// for every other test (a restore puts everyone out of it), nor the developer's.
/// </summary>
public sealed class ThrowawayDatabase : IAsyncDisposable
{
    private readonly string _masterConnectionString;

    private ThrowawayDatabase(string connectionString, string masterConnectionString)
    {
        ConnectionString = connectionString;
        _masterConnectionString = masterConnectionString;
    }

    public string ConnectionString { get; }

    public string Name => new SqlConnectionStringBuilder(ConnectionString).InitialCatalog;

    /// <summary>A new, migrated database named TinapayanRMS_Test_Backup_ and eight characters of a GUID.</summary>
    /// <param name="like">A connection string to the server to make it on, such as the fixture's.</param>
    public static async Task<ThrowawayDatabase> CreateAsync(string like, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(like) { InitialCatalog = $"TinapayanRMS_Test_Backup_{Guid.NewGuid():N}"[..33] };
        var master = new SqlConnectionStringBuilder(like) { InitialCatalog = "master", Pooling = false };
        var database = new ThrowawayDatabase(builder.ConnectionString, master.ConnectionString);

        await using var dbContext = database.CreateDbContext();
        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync("ALTER DATABASE CURRENT SET AUTO_CLOSE OFF", cancellationToken);
        return database;
    }

    public TinapayanRMSDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TinapayanRMSDbContext>().UseSqlServer(ConnectionString).Options);

    public IDbContextFactory<TinapayanRMSDbContext> CreateDbContextFactory() => new Factory(this);

    public async ValueTask DisposeAsync()
    {
        SqlConnection.ClearAllPools();
        await using var connection = new SqlConnection(_masterConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "IF DB_ID(@name) IS NOT NULL BEGIN "
            + "DECLARE @sql nvarchar(max) = N'ALTER DATABASE ' + QUOTENAME(@name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(@name); "
            + "EXEC (@sql); END",
            connection);
        command.Parameters.AddWithValue("@name", Name);
        await command.ExecuteNonQueryAsync();
    }

    private sealed class Factory(ThrowawayDatabase database) : IDbContextFactory<TinapayanRMSDbContext>
    {
        public TinapayanRMSDbContext CreateDbContext() => database.CreateDbContext();
    }
}
