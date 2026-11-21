using System.Globalization;
using System.Reflection;
using System.Runtime;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.LifecycleEvents;
using Serilog;
using Syncfusion.Blazor;
using Syncfusion.Licensing;
using TinapayanRMS.Components;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Components.Shared;
using TinapayanRMS.Maui.Services;
using TinapayanRMS.Maui.WinUI;

namespace TinapayanRMS.Maui;

/// <summary>
/// Builds the app: the log, the Syncfusion licence, the configuration and the app's services, then the
/// window's own (its file dialogs, its title bar, its close question). The hourly backups (StartupTasks) start once the
/// app is built and stop as its window closes (BackgroundWork).
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// The app's folder in the Windows user's app data: its view state and its log. The backups are in a folder every
    /// Windows user on the PC shares (DatabaseBackups.DefaultFolder).
    /// </summary>
    public static string DataFolder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinapayanRMS", "Maui");

    /// <summary>
    /// How the screens were left, for this Windows user.
    /// </summary>
    public static string ViewStateFile { get; } = Path.Combine(DataFolder, "view-state.json");

    /// <summary>
    /// This copy on the disk, as a key: a hash of its folder. Each copy has its own claim to run (SingleInstance) and its
    /// own web view folder.
    /// </summary>
    public static string CopyKey { get; } =
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(AppContext.BaseDirectory.ToUpperInvariant())))[..16];

    /// <summary>
    /// The web view's own folder (WebView2's cache and profile), for this Windows user and this copy. Left to itself it
    /// goes beside the .exe, which in a per-machine install is under Program Files, where the user can't write, so the
    /// page never loads. One per copy, as beside the .exe it was: WebView2 won't start a second process on a folder in use
    /// with other options, such as a check's debugging port while the user's own copy is open. Given to WebView2 as its
    /// WEBVIEW2_USER_DATA_FOLDER variable: BlazorWebViewInitializing's UserDataFolder goes into an environment MAUI makes
    /// and then doesn't hand the WinUI control, which starts with a default one of its own.
    /// </summary>
    public static string WebViewFolder { get; } = Path.Combine(DataFolder, "WebView2", CopyKey);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1506:Avoid excessive class coupling", Justification = "A composition root couples to everything it registers, by design.")]
    public static MauiApp CreateMauiApp()
    {
        // Syncfusion's grid and charts report their use to Syncfusion, over HTTPS (Syncfusion.Telemetry). Off before any
        // Syncfusion type is touched, at the user's request (2026-11-06).
        Syncfusion.Telemetry.Telemetry.Disable();

        // Numbers shown as the number boxes read them, whatever the Windows region writes them with: before anything is drawn.
        NumberCulture.Apply();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            // EF Core logs every SQL command at Information: the log keeps to what went wrong.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(
                Path.Combine(DataFolder, "logs", "tinapayanrms-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();

        // An exception that ends the process, a task that failed with nothing awaiting it, and a command run nothing awaited
        // that failed, are logged from here on: MAUI logs none of them. The window's own thread is hooked by the WinUI app
        // (Platforms/Windows/App).
        UnhandledFailures.Watch();

        // Before the web view starts, which reads it from the process's environment. One set already (a check's) wins.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")))
        {
            Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", WebViewFolder);
        }

        Log.Information("TinapayanRMS {Version} is starting", AppVersion.Current);

        // Which garbage collector this copy runs on: Satori's in a build with -p:UseSatoriGC=true (the Maui project's
        // experiment), which lists its settings under its own name, or .NET's own.
        Log.Information(
            "Garbage collector: {Collector}, latency mode {LatencyMode}",
            GC.GetConfigurationVariables().ContainsKey("SatoriGC") ? "Satori" : ".NET's own",
            GCSettings.LatencyMode);

        try
        {
            RegisterSyncfusionLicence();

            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();

            // appsettings.json, built into the app, then environment variables
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
#if DEBUG
            // A check in the window (the run-tinapayanrms skill) starts signed in, with no auto-lock: it can't know the
            // user's password, and it drives the page by script, which the auto-lock wouldn't see as a key or a click. A
            // Debug build only: the installed app is a Release one, which always asks.
            if (Environment.GetEnvironmentVariable("TINAPAYANRMS_SIGNED_IN") == "1")
            {
                var appLock = app.Services.GetRequiredService<TinapayanRMS.Presentation.App.AppLock>();
                appLock.SetAutoLockMinutes(0);
                appLock.Unlock();
                Log.Warning("Started signed in, for a check (TINAPAYANRMS_SIGNED_IN)");
            }
#endif
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
    /// so an installed app needs no variable; else the variable itself. Without either the app still
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
