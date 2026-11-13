using System.Globalization;
using System.Reflection;
using System.Runtime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Serilog;
using Syncfusion.Blazor;
using Syncfusion.Licensing;
using TinapayanRMS.Components;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Infrastructure.Services;
using TinapayanRMS.Maui.Services;
using TinapayanRMS.Maui.WinUI;

namespace TinapayanRMS.Maui;

/// <summary>
/// Builds the app: the log, the Syncfusion licence, ReactiveUI, the configuration and the app's services, then the
/// window's own (its file dialogs, its title bar, its close question). The hourly backups (StartupTasks) start once the
/// app is built and stop as its window closes (BackgroundWork).
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// The MAUI app's folder in the Windows user's app data, beside the WPF app's view-state.json: its own view state and
    /// its log. The backups are the WPF app's, since it's the same database (DatabaseBackups.DefaultFolder).
    /// </summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinapayanRMS", "Maui");

    /// <summary>
    /// How the screens were left, for this Windows user. The MAUI app's own: each app reads its file once and writes it
    /// whole, so two sharing one would write over each other's changes. It starts as a copy of the WPF app's
    /// (<see cref="ViewStateSeed"/>).
    /// </summary>
    public static string ViewStateFile { get; } = Path.Combine(DataFolder, "view-state.json");

    public static MauiApp CreateMauiApp()
    {
        // Syncfusion's grid and charts report their use to Syncfusion, over HTTPS (Syncfusion.Telemetry), as the WPF
        // controls did. Off before any Syncfusion type is touched, as the WPF app had it (2026-11-06).
        Syncfusion.Telemetry.Telemetry.Disable();

        // Numbers shown as the number boxes read them, whatever the Windows region writes them with: before anything is drawn.
        NumberCulture.Apply();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            // EF Core logs every SQL command at Information; the WPF app's log never carried them.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(DataFolder, "logs", "tinapayanrms-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        // An exception that ends the process, and a task that failed with nothing awaiting it, are logged from here on: MAUI
        // logs neither. The window's own thread is hooked by the WinUI app (Platforms/Windows/App), ReactiveUI's by its
        // bootstrapper.
        UnhandledFailures.Watch();

        // Which garbage collector this copy runs on: Satori's in a build with -p:UseSatoriGC=true (the Maui project's
        // experiment), which lists its settings under its own name, or .NET's own.
        Log.Information(
            "Garbage collector: {Collector}, latency mode {LatencyMode}",
            GC.GetConfigurationVariables().ContainsKey("SatoriGC") ? "Satori" : ".NET's own",
            GCSettings.LatencyMode);

        try
        {
            RegisterSyncfusionLicence();
            ReactiveUIBootstrapper.EnsureInitialized();
            ViewStateSeed.StartFrom(JsonViewStateStore.DefaultFilePath, ViewStateFile);

            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();

            // appsettings.json, built into the app (the WPF app's file), then environment variables
            // (ConnectionStrings__Default) over it.
            // The configuration reads the stream as it's added, so it can go straight after.
            using (var settings = typeof(MauiProgram).Assembly.GetManifestResourceStream("appsettings.json")
                ?? throw new InvalidOperationException("The app's appsettings.json isn't built into it."))
            using (var copy = new MemoryStream())
            {
                settings.CopyTo(copy);
                copy.Position = 0;
                builder.Configuration.AddJsonStream(copy);
            }

            builder.Configuration.AddEnvironmentVariables();

            builder.Logging.ClearProviders();
            builder.Logging.AddSerilog(Log.Logger);

            builder.Services.AddMauiBlazorWebView();
#if DEBUG
            // F12 in the window, as in a browser.
            builder.Services.AddBlazorWebViewDeveloperTools();
#endif
            builder.Services.AddSyncfusionBlazor();
            builder.Services.AddTinapayanRMS(builder.Configuration, ViewStateFile);
            builder.Services.AddSingleton<IFileDialogs, FileDialogs>();
            builder.Services.AddSingleton<AppWindowChrome>();
            builder.Services.AddSingleton<IAppWindow>(provider => provider.GetRequiredService<AppWindowChrome>());
            builder.ConfigureLifecycleEvents(events => events.AddWindows(windows => windows
                .OnWindowCreated(WindowsWindow.Configure)
                .OnPlatformMessage(WindowsWindow.OnMessage)));

            var app = builder.Build();
            BackgroundWork.Start(app.Services);
            return app;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "The app stopped because of an error at startup");
            Log.CloseAndFlush();

            // A MAUI app that throws as it starts just disappears: say why, and where the details are.
            StartupMessage.ShowError(
                $"TinapayanRMS couldn't start: {Failure.FirstLine(ex.Message)}{Environment.NewLine}{Environment.NewLine}"
                + $"The details are in its log, in {Path.Combine(DataFolder, "logs")}.");
            throw;
        }
    }

    /// <summary>
    /// The key the build compiled in from the SYNCFUSION_LICENSE_KEY environment variable (the project's AssemblyMetadata),
    /// so an installed app needs no variable, as the WPF app did; else the variable itself. Without either the app still
    /// opens, and Syncfusion says in the window that it has no licence.
    /// </summary>
    private static void RegisterSyncfusionLicence()
    {
        var key = typeof(MauiProgram).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "SyncfusionLicenseKey")?.Value
            ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY");
        if (string.IsNullOrWhiteSpace(key))
        {
            Log.Error("No Syncfusion licence key: it wasn't built into the app, and SYNCFUSION_LICENSE_KEY isn't set");
            return;
        }

        SyncfusionLicenseProvider.RegisterLicense(key.Trim());
    }
}
