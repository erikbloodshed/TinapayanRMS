using AwesomeAssertions;
using Npgsql;
using TinapayanRMS.Infrastructure.Tests.Fixtures;

namespace TinapayanRMS.Infrastructure.Tests.Persistence;

/// <summary>
/// A restore has to wait for connections it can't end, such as the Recipe API's as its reader role, and gives up after 30
/// seconds (DatabaseBackups). A read-only connection left idle in its pool has to have closed by itself before then. Its
/// pool is a data source of its own, with the read-only connection's settings: the tests that clear every pool
/// (NpgsqlConnection.ClearAllPools, in collections A and B) run beside it, and a clear reaches only the pools Npgsql keeps
/// per connection string, where one closed the connection before it was counted (2026-11-25).
/// </summary>
[NotInParallel(DatabaseCollection.D)]
[ClassDataSource<DatabaseFixture>(Shared = SharedType.Keyed, Key = DatabaseCollection.D)]
public class ReadOnlyConnectionIdleTests(DatabaseFixture fixture)
{
    private static readonly TimeSpan RestoreWait = TimeSpan.FromSeconds(25);

    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task A_connection_left_idle_closes_by_itself_within_what_a_restore_waits()
    {
        var name = $"idle-{Guid.NewGuid():N}";
        var pooled = new NpgsqlConnectionStringBuilder(ReadOnlyConnection.Of(fixture.ConnectionString)) { ApplicationName = name };

        // Kept to the end: disposing a data source closes its connections, which is what the test waits for the pool to do.
        await using var dataSource = NpgsqlDataSource.Create(pooled.ConnectionString);
        await using (await dataSource.OpenConnectionAsync(Cancellation))
        {
        }

        (await OpenConnectionsAsync(name)).Should().Be(1, "closing it only hands it back to its pool, still open on the server");

        var giveUpAt = DateTime.UtcNow + RestoreWait;
        while (await OpenConnectionsAsync(name) > 0)
        {
            DateTime.UtcNow.Should().BeBefore(giveUpAt, "a restore gives up on a connection that stays open");
            await Task.Delay(TimeSpan.FromMilliseconds(250), Cancellation);
        }
    }

    private async Task<long> OpenConnectionsAsync(string applicationName)
    {
        var unpooled = new NpgsqlConnectionStringBuilder(fixture.ConnectionString) { Pooling = false };
        await using var connection = new NpgsqlConnection(unpooled.ConnectionString);
        await connection.OpenAsync(Cancellation);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE application_name = @name", connection);
        command.Parameters.AddWithValue("name", applicationName);
        return (long)(await command.ExecuteScalarAsync(Cancellation))!;
    }
}
