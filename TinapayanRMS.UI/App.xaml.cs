using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;

namespace TinapayanRMS.UI;

public partial class App : Application
{
    private IHost? _host;

    static App()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                Path.Combine(AppContext.BaseDirectory, "logs", "tinapayanrms-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31)
            .CreateLogger();

        try
        {
            var licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")
                ?? throw new InvalidOperationException("The SYNCFUSION_LICENSE_KEY environment variable is not set.");

            SyncfusionLicenseProvider.RegisterLicense(licenseKey);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Startup failed before the main window could be created");
            throw;
        }
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        _host = CompositionRoot.CreateHostBuilder(e.Args).Build();
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        SfSkinManager.SetTheme(mainWindow, new Theme("Windows11Light"));
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        Log.CloseAndFlush();
        base.OnExit(e);
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled exception on the UI thread");
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception ({IsTerminating})", e.IsTerminating);
    }
}
