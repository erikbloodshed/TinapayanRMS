using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.RecipeApi;

/// <summary>
/// The Recipe API as the Windows service the app's MSI installs (installer/Package.wxs, its Recipe API feature), on the
/// PC that holds the database (ADR 0021). It runs as its own virtual account, NT SERVICE\TinapayanRMSRecipeApi, so nothing
/// in a Windows user's profile is its: what it knows of this PC is in <see cref="DataFolder"/>, which
/// scripts/Setup-RecipeApi.ps1 fills and locks to the administrators and the service. Its settings there go over the ones beside it, and its
/// connection takes its password from the password file there. Under dotnet run and in the tests it isn't a service, and
/// none of this applies.
/// </summary>
public static class RecipeApiService
{
    /// <summary>The service's name, as the MSI installs it and the scripts find it.</summary>
    public const string Name = "TinapayanRMSRecipeApi";

    /// <summary>The settings file's name, beside the API and in <see cref="DataFolder"/>.</summary>
    public const string SettingsFileName = "recipeapi.json";

    /// <summary>The password file's name in <see cref="DataFolder"/>, in pgpass.conf's form.</summary>
    public const string PasswordFileName = "pgpass.conf";

    /// <summary>%ProgramData%\TinapayanRMS\RecipeApi: the service's key, role, password file and logs.</summary>
    public static string DataFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TinapayanRMS", "RecipeApi");

    /// <summary>Where the API logs: in <see cref="DataFolder"/> as the service, and beside it otherwise.</summary>
    public static string LogFolder(bool asService) =>
        Path.Combine(asService ? DataFolder : AppContext.BaseDirectory, "logs");

    /// <summary>
    /// Runs the host as the service when Windows started it as one (UseWindowsService does nothing otherwise), with the
    /// settings in <paramref name="dataFolder"/> (<see cref="DataFolder"/> by default) over those beside it, and the password
    /// file there for its connection (<see cref="ReadOnlyConnection.PasswordFileKey"/>). Add it before
    /// <see cref="ReadOnlyConnection.Apply"/>, which reads both.
    /// </summary>
    public static IHostBuilder AsWindowsService(this IHostBuilder builder, string? dataFolder = null)
    {
        var folder = dataFolder ?? DataFolder;
        return builder
            .UseWindowsService(options => options.ServiceName = Name)
            .ConfigureAppConfiguration(config =>
            {
                config.AddJsonFile(Path.Combine(folder, SettingsFileName), optional: true, reloadOnChange: false);
                config.AddInMemoryCollection([new(ReadOnlyConnection.PasswordFileKey, Path.Combine(folder, PasswordFileName))]);
            });
    }
}
