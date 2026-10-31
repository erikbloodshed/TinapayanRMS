using System.Globalization;
using System.IO;
using Microsoft.Extensions.Hosting;
using Serilog;
using TinapayanRMS.RecipeApi;
using TinapayanRMS.UI;

// The app's own host (CompositionRoot), so every repository and reader is registered as the screens' are, with the
// Recipe API's web server on top. Its database connection is read-only (ReadOnlyConnection), so PostgreSQL itself
// refuses a write. It logs to a file beside it, as the app does.
Log.Logger = new LoggerConfiguration()
    .WriteTo.File(
        Path.Combine(AppContext.BaseDirectory, "logs", "tinapayanrms-recipeapi-.log"),
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 31,
        formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

try
{
    await CompositionRoot.CreateHostBuilder(args)
        .AddRecipeApi()
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
