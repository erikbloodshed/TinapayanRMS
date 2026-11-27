using System.Globalization;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.WindowsServices;
using Serilog;
using TinapayanRMS.Infrastructure;
using TinapayanRMS.RecipeApi;

// The host the app starts from too (DatabaseHost), so every repository is registered as the screens' are, with the
// Recipe API's web server on top. Its database connection is read-only (ReadOnlyConnection), so PostgreSQL itself
// refuses a write. Installed, it's a Windows service (RecipeApiService), with its settings and its log in
// %ProgramData%\TinapayanRMS\RecipeApi; run from source, it logs to a file beside it.
var asService = WindowsServiceHelpers.IsWindowsService();
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(RecipeApiService.LogFolder(asService), "tinapayanrms-recipeapi-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,
        formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    var host = DatabaseHost.CreateBuilder().AddRecipeApi();
    if (asService)
    {
        host = host.AsWindowsService();
    }

    await host
        .ConfigureAppConfiguration(ReadOnlyConnection.Apply)
        .UseSerilog()
        .Build()
        .RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "The Recipe API stopped");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
