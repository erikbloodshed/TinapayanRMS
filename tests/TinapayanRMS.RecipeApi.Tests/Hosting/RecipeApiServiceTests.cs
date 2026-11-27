using AwesomeAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.RecipeApi.Tests.Hosting;

/// <summary>
/// The settings the Recipe API runs with as the MSI's service (RecipeApiService), read from a folder of the test's own in
/// place of %ProgramData%\TinapayanRMS\RecipeApi: the host is built as Program builds it, never started, so no database is
/// reached.
/// </summary>
public class RecipeApiServiceTests
{
    [Test]
    public async Task Its_settings_go_over_the_ones_beside_it_and_it_signs_in_with_its_own_password_file()
    {
        using var folder = new ScratchFolder();
        await File.WriteAllTextAsync(
            Path.Combine(folder.Path, RecipeApiService.SettingsFileName),
            """{ "RecipeApi": { "Key": "the-service-key" }, "Database": { "ReadOnlyUser": "tinapayanrms_reader" } }""");

        using var host = Build(folder.Path);

        var settings = host.Services.GetRequiredService<IConfiguration>();
        settings["RecipeApi:Key"].Should().Be("the-service-key");
        var connection = new NpgsqlConnectionStringBuilder(settings.GetConnectionString("Default"));
        connection.Username.Should().Be("tinapayanrms_reader");
        connection.Passfile.Should().Be(Path.Combine(folder.Path, RecipeApiService.PasswordFileName));
        connection.Options.Should().Be("-c default_transaction_read_only=on");
    }

    [Test]
    public void Before_its_setup_it_has_no_key_and_still_signs_in_with_its_own_password_file()
    {
        using var folder = new ScratchFolder();

        using var host = Build(folder.Path);

        var settings = host.Services.GetRequiredService<IConfiguration>();
        settings["RecipeApi:Key"].Should().BeEmpty("the one beside it is blank, so it answers only its own PC");
        var connection = new NpgsqlConnectionStringBuilder(settings.GetConnectionString("Default"));
        connection.Username.Should().Be("tinapayanrms", "the app's own role, read-only");
        connection.Passfile.Should().Be(Path.Combine(folder.Path, RecipeApiService.PasswordFileName));
    }

    [Test]
    public void Its_log_is_in_its_data_folder_as_the_service_and_beside_it_otherwise()
    {
        RecipeApiService.LogFolder(asService: true).Should().Be(Path.Combine(RecipeApiService.DataFolder, "logs"));
        RecipeApiService.LogFolder(asService: false).Should().Be(Path.Combine(AppContext.BaseDirectory, "logs"));
        RecipeApiService.DataFolder.Should().Be(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "RecipeApi"));
    }

    /// <summary>The host as Program builds it for the service, with <paramref name="dataFolder"/> as its data folder.</summary>
    private static IHost Build(string dataFolder) =>
        DatabaseHost.CreateBuilder()
            .AddRecipeApi(web => web.UseTestServer())
            .AsWindowsService(dataFolder)
            .ConfigureAppConfiguration(ReadOnlyConnection.Apply)
            .Build();

    private sealed class ScratchFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("tinapayanrms-recipeapi-").FullName;

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
