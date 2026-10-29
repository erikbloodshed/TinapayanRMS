using System.IO;
using AwesomeAssertions;
using Npgsql;
using TinapayanRMS.Infrastructure.Services;

namespace TinapayanRMS.Infrastructure.Tests.Services;

/// <summary>
/// PostgresTools running PostgreSQL's own tools as the backups do: what a tool writes out comes back, one that fails
/// throws what it wrote, and a cancelled run throws. None of them reaches the server: pg_dump's <c>--version</c> and
/// pg_restore's <c>--list</c> of a file don't connect.
/// </summary>
public sealed class PostgresToolsTests
{
    private static readonly NpgsqlConnectionStringBuilder Connection = new("Host=localhost;Username=tinapayanrms");

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_tool_that_succeeds_returns_what_it_wrote()
    {
        var output = await new PostgresTools(null).RunAsync("pg_dump", Connection, ["--version"], Cancellation);

        output.Should().StartWith("pg_dump (PostgreSQL)");
    }

    [Fact]
    public async Task A_tool_that_fails_throws_what_it_wrote()
    {
        var notABackup = Path.Combine(Path.GetTempPath(), $"not a backup {Guid.NewGuid():N}.backup");
        await File.WriteAllTextAsync(notABackup, "Pandesal, 4.50", Cancellation);
        try
        {
            var list = () => new PostgresTools(null).RunAsync("pg_restore", Connection, ["--list", notABackup], Cancellation);

            (await list.Should().ThrowAsync<PostgresToolException>())
                .Which.Error.Should().Contain("does not appear to be a valid archive");
        }
        finally
        {
            File.Delete(notABackup);
        }
    }

    [Fact]
    public async Task A_cancelled_run_throws()
    {
        var run = () => new PostgresTools(null).RunAsync("pg_dump", Connection, ["--version"], new CancellationToken(canceled: true));

        await run.Should().ThrowAsync<OperationCanceledException>();
    }
}
