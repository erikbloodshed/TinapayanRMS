using Microsoft.Extensions.Configuration;
using Npgsql;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// The server the benchmarks' databases are made on, the app's own (appsettings.json), and their names: tinapayanrms_bench_,
/// never the developer's database. Apart from BenchDatabases, so a cold start's sample loads nothing a start of the app
/// doesn't (the test fixtures' assembly).
/// </summary>
public static class BenchServer
{
    /// <summary>What every database a run makes is named with: the one name <see cref="Ensure"/> lets a command touch.</summary>
    public const string Prefix = "tinapayanrms_bench_";

    /// <summary>A connection string to the server's maintenance database, postgres: the app's server, its database aside.</summary>
    public static string Maintenance
    {
        get
        {
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build();
            return new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Default")) { Database = "postgres" }.ConnectionString;
        }
    }

    /// <summary>The app's connection string for <paramref name="database"/>, one of the benchmarks' (<see cref="Ensure"/>).</summary>
    public static string ConnectionStringOf(string database)
    {
        Ensure(database);
        return new NpgsqlConnectionStringBuilder(Maintenance) { Database = database }.ConnectionString;
    }

    /// <summary>Refuses a database that isn't one of a run's: nothing here may reach the developer's, or a test's.</summary>
    public static void Ensure(string name)
    {
        if (!name.StartsWith(Prefix, StringComparison.Ordinal) || name.Length > 63 || !name.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
        {
            throw new ArgumentException($"{name} isn't a benchmark database: their names start {Prefix}, in lower case letters, digits and underscores.");
        }
    }
}
