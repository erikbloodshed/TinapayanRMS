using AwesomeAssertions;
using Npgsql;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Infrastructure.Tests.Services;

/// <summary>
/// PostgresTools running PostgreSQL's own tools as the backups do: what a tool writes out comes back, one that fails
/// throws what it wrote, and a cancelled run throws. Only the first reaches the server, reading the schema of its
/// maintenance database: every run is given --no-password, and pg_dump's <c>--version</c> only works as its first argument.
/// Where the tools are looked for, too. That a run can't wait at a prompt, and is stopped in time, is in DatabaseBackupsTests.
/// </summary>
public sealed class PostgresToolsTests
{
    private static readonly NpgsqlConnectionStringBuilder Connection = new("Host=localhost;Username=tinapayanrms");

    private static CancellationToken Cancellation => TestContext.Current!.Execution.CancellationToken;

    [Test]
    public async Task A_tool_that_succeeds_returns_what_it_wrote()
    {
        var output = await new PostgresTools(null).RunAsync("pg_dump", Connection, ["--schema-only", "--dbname", "postgres"], Cancellation);

        output.Should().Contain("PostgreSQL database dump");
    }

    [Test]
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

    [Test]
    public async Task A_cancelled_run_throws()
    {
        var run = () => new PostgresTools(null).RunAsync("pg_dump", Connection, ["--version"], new CancellationToken(canceled: true));

        await run.Should().ThrowAsync<OperationCanceledException>();
    }

    // Backups:ToolsFolder, for tools somewhere the search doesn't look.
    [Test]
    public void The_folder_set_in_the_settings_is_looked_in_first_and_the_path_last()
    {
        var configured = Path.Combine(Path.GetTempPath(), "Tools", "PostgreSQL");

        var folders = PostgresTools.CandidateFolders(configured).ToList();

        folders[0].Should().Be(configured);
        var path = Environment.GetEnvironmentVariable("PATH")!.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        folders.TakeLast(path.Length).Should().Equal(path);
    }

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    public void A_blank_folder_counts_as_none_set(string configured) =>
        PostgresTools.CandidateFolders(configured).Should().Equal(PostgresTools.CandidateFolders(null));

    // A folder set that has no tools in it, such as one left over from an old install, isn't the end of it.
    [Test]
    public async Task A_folder_set_without_the_tools_falls_back_on_the_search()
    {
        var empty = Directory.CreateTempSubdirectory("no-postgres-tools-").FullName;
        var notABackup = Path.Combine(empty, "not a backup.backup");
        await File.WriteAllTextAsync(notABackup, "Pandesal, 4.50", Cancellation);
        try
        {
            var list = () => new PostgresTools(empty).RunAsync("pg_restore", Connection, ["--list", notABackup], Cancellation);

            (await list.Should().ThrowAsync<PostgresToolException>("it ran the installed pg_restore, on a file that isn't a backup"))
                .Which.Error.Should().Contain("does not appear to be a valid archive");
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }
}
