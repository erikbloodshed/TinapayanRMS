namespace TinapayanRMS.ApplicationCore.Interfaces;

/// <summary>
/// A PostgreSQL tool (pg_dump, pg_restore) that exited with an error, with what it wrote as its message: what
/// <see cref="IDatabaseBackups"/> throws when one stops, which the screens put in its own words (Rules.Failure).
/// </summary>
public sealed class PostgresToolException(string tool, string error) : Exception($"{tool} failed: {error}")
{
    public string Tool { get; } = tool;

    public string Error { get; } = error;
}
