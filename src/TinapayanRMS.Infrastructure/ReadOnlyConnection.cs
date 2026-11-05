using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace TinapayanRMS.Infrastructure;

/// <summary>
/// Makes a server's database connection read-only: every transaction on it starts read-only
/// (default_transaction_read_only), so PostgreSQL refuses an INSERT, UPDATE or DELETE whatever code runs it. The MCP
/// server and the Recipe API read the database the app saves to, and only read, but this doesn't rest on that. Both build
/// their hosts from DatabaseHost, so it sits beside it. With <see cref="UserKey"/> set, as the installed Recipe API has it,
/// the connection is also made as a role that can only read (scripts/New-DatabaseReader.ps1), which no code on the
/// connection can turn the read-only transactions off for.
/// </summary>
public static class ReadOnlyConnection
{
    /// <summary>
    /// The setting naming the PostgreSQL role to connect as, such as tinapayanrms_reader, whose password is in the Windows
    /// user's pgpass.conf as the app's own role's is. Unset, the app's own role, in read-only transactions all the same.
    /// </summary>
    public const string UserKey = "Database:ReadOnlyUser";

    /// <summary><paramref name="connectionString"/>, its transactions read-only, and as <paramref name="user"/> when one is given.</summary>
    public static string Of(string connectionString, string? user = null)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Options = "-c default_transaction_read_only=on" };
        if (!string.IsNullOrWhiteSpace(user))
        {
            builder.Username = user.Trim();
        }

        return builder.ConnectionString;
    }

    /// <summary>
    /// Puts a read-only copy of the app's connection string (appsettings.json's, which DatabaseHost added) over it, as the
    /// role <see cref="UserKey"/> names when it's set.
    /// </summary>
    public static void Apply(HostBuilderContext context, IConfigurationBuilder config)
    {
        var settings = config.Build();
        var connectionString = settings.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");
        config.AddInMemoryCollection([new("ConnectionStrings:Default", Of(connectionString, settings[UserKey]))]);
    }
}
