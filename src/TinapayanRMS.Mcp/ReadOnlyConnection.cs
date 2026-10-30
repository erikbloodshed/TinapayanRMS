using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace TinapayanRMS.Mcp;

/// <summary>
/// Makes the server's database connection read-only: every transaction on it starts read-only
/// (default_transaction_read_only), so PostgreSQL refuses an INSERT, UPDATE or DELETE whatever code runs it. The server
/// reads the dev database the app saves to, and its tools only read, but this doesn't rest on that.
/// </summary>
public static class ReadOnlyConnection
{
    /// <summary><paramref name="connectionString"/>, its transactions read-only.</summary>
    public static string Of(string connectionString) =>
        new NpgsqlConnectionStringBuilder(connectionString) { Options = "-c default_transaction_read_only=on" }.ConnectionString;

    /// <summary>Puts a read-only copy of the app's connection string (appsettings.json's, which CompositionRoot added) over it.</summary>
    public static void Apply(HostBuilderContext context, IConfigurationBuilder config)
    {
        var connectionString = config.Build().GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");
        config.AddInMemoryCollection([new("ConnectionStrings:Default", Of(connectionString))]);
    }
}
