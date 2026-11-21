using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Syncfusion.Blazor;
using TinapayanRMS.Components;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// The app's services as the window builds them (MauiProgram: Syncfusion's, then ServiceRegistration.AddTinapayanRMS), on
/// one of the run's databases, so a scenario measures the code the app runs, wired as it's wired: a refactor that changes a
/// constructor changes nothing here. What only the window gives (its file dialogs, its title bar) isn't asked for by
/// anything a scenario runs. With no host, nothing registered as a hosted service (StartupTasks' hourly backups) starts.
/// </summary>
public static class AppServices
{
    /// <summary>The folder a run keeps its files in (the screens' view state, the cold start's log): deleted as it ends.</summary>
    public static string RunFolder { get; } = Path.Combine(Path.GetTempPath(), "trms-bench", $"run-{Environment.ProcessId}");

    /// <summary>The app's services on <paramref name="connectionString"/>, with a view state of their own in the run's folder.</summary>
    public static ServiceProvider Build(string connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = connectionString })
            .Build();
        var services = new ServiceCollection();
        services.AddSyncfusionBlazor();
        services.AddTinapayanRMS(configuration, Path.Combine(RunFolder, $"view-state-{Guid.NewGuid():N}.json"));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Deletes the run's folder, as the run ends.</summary>
    public static void DeleteRunFolder()
    {
        try
        {
            if (Directory.Exists(RunFolder))
            {
                Directory.Delete(RunFolder, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held (the log, as the process ends): the temp folder's own clean-up takes it.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
