using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace TinapayanRMS.Infrastructure.Tests;

/// <summary>The connection string the Recipe API and the MCP server read the database with, on its own.</summary>
public class ReadOnlyConnectionTests
{
    private const string AppConnection = "Host=localhost;Database=tinapayanrms;Username=tinapayanrms";

    [Test]
    public void Every_transaction_is_read_only_as_the_apps_own_role_with_no_reader_set()
    {
        var connection = new NpgsqlConnectionStringBuilder(ReadOnlyConnection.Of(AppConnection));

        connection.Options.Should().Be("-c default_transaction_read_only=on");
        connection.Username.Should().Be("tinapayanrms");
        connection.Database.Should().Be("tinapayanrms");
    }

    [Test]
    public void A_reader_set_is_the_role_it_connects_as_still_read_only()
    {
        var connection = new NpgsqlConnectionStringBuilder(ReadOnlyConnection.Of(AppConnection, " tinapayanrms_reader "));

        connection.Username.Should().Be("tinapayanrms_reader");
        connection.Options.Should().Be("-c default_transaction_read_only=on");
        connection.Database.Should().Be("tinapayanrms", "the same database the app saves to");
    }

    [Test]
    public void A_password_file_set_is_the_one_it_signs_in_with()
    {
        var connection = new NpgsqlConnectionStringBuilder(
            ReadOnlyConnection.Of(AppConnection, "tinapayanrms_reader", @" C:\ProgramData\TinapayanRMS\RecipeApi\pgpass.conf "));

        connection.Passfile.Should().Be(@"C:\ProgramData\TinapayanRMS\RecipeApi\pgpass.conf");
        connection.Username.Should().Be("tinapayanrms_reader");
    }

    [Test]
    public void With_no_password_file_set_it_signs_in_with_the_users_own()
    {
        var connection = new NpgsqlConnectionStringBuilder(ReadOnlyConnection.Of(AppConnection));

        connection.Passfile.Should().BeNull("Npgsql then reads the Windows user's %APPDATA%\\postgresql\\pgpass.conf");
    }

    [Test]
    public void Apply_signs_in_with_the_password_file_its_settings_name()
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection([
            new("ConnectionStrings:Default", AppConnection),
            new(ReadOnlyConnection.PasswordFileKey, @"D:\pgpass.conf"),
        ]);

        ReadOnlyConnection.Apply(new HostBuilderContext(new Dictionary<object, object>()), builder);

        new NpgsqlConnectionStringBuilder(builder.Build().GetConnectionString("Default")).Passfile.Should().Be(@"D:\pgpass.conf");
    }

    [Test]
    [Arguments(null, "tinapayanrms")]
    [Arguments("", "tinapayanrms")]
    [Arguments("tinapayanrms_reader", "tinapayanrms_reader")]
    public void Apply_puts_the_read_only_connection_over_the_apps_as_the_reader_its_settings_name(string? reader, string connectsAs)
    {
        var builder = new ConfigurationBuilder().AddInMemoryCollection([
            new("ConnectionStrings:Default", AppConnection),
            new(ReadOnlyConnection.UserKey, reader),
        ]);

        ReadOnlyConnection.Apply(new HostBuilderContext(new Dictionary<object, object>()), builder);

        var connection = new NpgsqlConnectionStringBuilder(builder.Build().GetConnectionString("Default"));
        connection.Username.Should().Be(connectsAs);
        connection.Options.Should().Be("-c default_transaction_read_only=on");
    }
}
