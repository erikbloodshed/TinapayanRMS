using System.Diagnostics;
using Npgsql;
using TinapayanRMS.ApplicationCore.Rules;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// PostgreSQL's own command-line tools (pg_dump and pg_restore), which the backups run. They're found in
/// <paramref name="configuredFolder"/> when there is one, else in PostgreSQL's usual places on Windows and on a Mac, newest
/// version first, else on the PATH. Each runs with the app's connection details in its environment, and reads its password
/// from the same password file Npgsql does, so none is ever on a command line.
/// </summary>
/// <param name="configuredFolder">The folder holding the tools (Backups:ToolsFolder), or null to look for them.</param>
/// <param name="timeout">How long a tool may run before it's stopped: <see cref="DefaultTimeout"/> unless a test says otherwise.</param>
internal sealed class PostgresTools(string? configuredFolder, TimeSpan? timeout = null)
{
    /// <summary>
    /// Far longer than a backup or restore of this app's data takes (under a second for 11 MB), since a tool stopped too
    /// soon only means the next hour's check tries again, and one left waiting holds up every backup and restore after it.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    public const string NotFoundText =
        "Backups need PostgreSQL's pg_dump and pg_restore, and they weren't found on this computer. Install PostgreSQL's "
        + "command-line tools, or set Backups:ToolsFolder in appsettings.json to the folder that has them.";

    private readonly Lazy<string?> _folder = new(() => Find(configuredFolder));

    /// <summary>
    /// Runs <paramref name="tool"/> against the server <paramref name="connection"/> names and returns what it wrote out. A
    /// tool that fails throws <see cref="PostgresToolException"/> with what it wrote as its error; one that's cancelled is
    /// stopped, and so is one still running after the timeout, which throws <see cref="TimeoutException"/>. .NET 11's
    /// <see cref="Process.RunAndCaptureTextAsync(ProcessStartInfo, CancellationToken)"/> reads both of its pipes as it runs,
    /// so neither fills up and stalls it.
    /// <para>
    /// It's never let ask for a password (--no-password). With none available, in the password file or the connection
    /// string, a tool otherwise waits at a prompt on a console nobody can see, for ever, and every backup and restore
    /// after it waits its turn behind it.
    /// </para>
    /// </summary>
    public async Task<string> RunAsync(
        string tool, NpgsqlConnectionStringBuilder connection, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var folder = _folder.Value ?? throw new BusinessRuleException(NotFoundText);
        var startInfo = new ProcessStartInfo(Path.Combine(folder, ExecutableName(tool)))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        startInfo.ArgumentList.Add("--no-password");
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        SetConnection(startInfo.Environment, connection);

        // Cancelled while it's writing, it's stopped and this throws. Cancelled once it has written everything, it's
        // stopped too, and its exit status says so. The backups never ask for parallel jobs, so the tool is one process.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stop.CancelAfter(_timeout);
        ProcessTextOutput run;
        try
        {
            run = await Process.RunAndCaptureTextAsync(startInfo, stop.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw TimedOut(tool);
        }

        if (run.ExitStatus.Canceled)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw TimedOut(tool);
        }

        return run.ExitStatus.ExitCode == 0 ? run.StandardOutput : throw new PostgresToolException(tool, run.StandardError.Trim());
    }

    private TimeoutException TimedOut(string tool) => new($"{tool} was still running after {_timeout}, so it was stopped.");

    /// <summary>
    /// Where libpq looks for the server, the account and its password: the connection string's own, so the tools reach the
    /// database the app does. A password only goes in when the connection string carries one; otherwise libpq reads the
    /// password file itself (pgpass.conf on Windows, ~/.pgpass elsewhere), as Npgsql does.
    /// </summary>
    private static void SetConnection(IDictionary<string, string?> environment, NpgsqlConnectionStringBuilder connection)
    {
        Set("PGHOST", connection.Host);
        Set("PGPORT", connection.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Set("PGUSER", connection.Username);
        Set("PGPASSWORD", connection.Password);
        Set("PGPASSFILE", connection.Passfile);
        environment["PGAPPNAME"] = "TinapayanRMS backups";
        environment.Remove("PGDATABASE");

        void Set(string name, string? value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                environment[name] = value;
            }
        }
    }

    private static string ExecutableName(string tool) => OperatingSystem.IsWindows() ? tool + ".exe" : tool;

    /// <summary>The first of <see cref="CandidateFolders"/> that has both tools.</summary>
    private static string? Find(string? configuredFolder) =>
        CandidateFolders(configuredFolder).FirstOrDefault(folder =>
            File.Exists(Path.Combine(folder, ExecutableName("pg_dump"))) && File.Exists(Path.Combine(folder, ExecutableName("pg_restore"))));

    /// <summary>
    /// Where the tools may be, in the order they're looked for: the configured folder; then the folders PostgreSQL's
    /// installers use, newest version first, since an older pg_dump refuses a newer server; then the PATH.
    /// </summary>
    internal static IEnumerable<string> CandidateFolders(string? configuredFolder)
    {
        if (!string.IsNullOrWhiteSpace(configuredFolder))
        {
            yield return configuredFolder;
        }

        if (OperatingSystem.IsWindows())
        {
            // EDB's installer: C:\Program Files\PostgreSQL\18\bin.
            foreach (var folder in VersionedFolders(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PostgreSQL"), "bin"))
            {
                yield return folder;
            }
        }
        else
        {
            // Postgres.app, then Homebrew's versioned formulas on Apple silicon and Intel, then its unversioned links.
            yield return "/Applications/Postgres.app/Contents/Versions/latest/bin";
            foreach (var prefix in (string[])["/opt/homebrew/opt", "/usr/local/opt"])
            {
                foreach (var folder in VersionedFolders(prefix, "bin", "postgresql@"))
                {
                    yield return folder;
                }
            }

            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";

            // Debian and Ubuntu: /usr/lib/postgresql/18/bin.
            foreach (var folder in VersionedFolders("/usr/lib/postgresql", "bin"))
            {
                yield return folder;
            }
        }

        foreach (var folder in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return folder;
        }
    }

    /// <summary>
    /// <paramref name="parent"/>'s subfolders named <paramref name="prefix"/> and a version number, highest version first,
    /// each with <paramref name="child"/> appended.
    /// </summary>
    private static IEnumerable<string> VersionedFolders(string parent, string child, string prefix = "")
    {
        if (!Directory.Exists(parent))
        {
            return [];
        }

        return Directory.EnumerateDirectories(parent, prefix + "*")
            .Select(folder => (Folder: folder, Version: Version.TryParse(VersionText(Path.GetFileName(folder)[prefix.Length..]), out var version) ? version : null))
            .Where(candidate => candidate.Version is not null)
            .OrderByDescending(candidate => candidate.Version)
            .Select(candidate => Path.Combine(candidate.Folder, child));

        // "18" isn't a Version on its own; "18.0" is.
        static string VersionText(string name) => name.Contains('.', StringComparison.Ordinal) ? name : name + ".0";
    }
}
