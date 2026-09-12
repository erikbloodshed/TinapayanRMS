using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;
using Syncfusion.Themes.Windows11Light.WPF;
using Syncfusion.Windows.Controls.Input;

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

        // The Windows11Light theme's SfTextBoxExt style — unlike its ComboBoxAdv, DoubleTextBox and
        // DateTimeEdit styles — never sets VerticalContentAlignment, so text falls back to
        // Control's default of Top and sits against the top of the box at the 32px
        // SizeMode="Touch" height. Changing the type's default here fixes every instance,
        // including future ones, without a Style on the control. Must run before any
        // SfTextBoxExt exists, which is why it lives in this static constructor.
        Control.VerticalContentAlignmentProperty.OverrideMetadata(
            typeof(SfTextBoxExt), new FrameworkPropertyMetadata(VerticalAlignment.Center));
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

        // Re-colors the theme's primary accent (buttons, grid selection, tab/drawer
        // indicators, focus rings) to the app's warm amber. PrimaryBackground's setter
        // derives the Dark1-3/Light1-3/Opacity1-3 shades, so this one property is enough.
        //
        // ApplyThemeAsDefaultStyle is deliberately NOT set: turning it on makes SfSkinManager
        // re-style every open Window as a ChromelessWindow (see SfSkinManager.ApplyWindowStyle),
        // replacing the native title bar with Syncfusion's custom chrome. Without it, Syncfusion
        // controls still pick up the theme and accent via SetTheme below; only plain framework
        // controls (CheckBox, Expander, ProgressBar) keep the OS default look.
        SfSkinManager.RegisterThemeSettings("Windows11Light", new Windows11LightThemeSettings
        {
            PrimaryBackground = new SolidColorBrush(Color.FromRgb(0xB4, 0x53, 0x09)),
            PrimaryForeground = Brushes.White,
        });

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
