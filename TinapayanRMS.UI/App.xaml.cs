using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;

namespace TinapayanRMS.UI;

public partial class App : Application
{
    private IHost? _host;

    static App()
    {
        var licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY")
            ?? throw new InvalidOperationException("The SYNCFUSION_LICENSE_KEY environment variable is not set.");

        SyncfusionLicenseProvider.RegisterLicense(licenseKey);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _host = CompositionRoot.CreateHostBuilder(e.Args).Build();
        _host.Start();

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        SfSkinManager.SetTheme(mainWindow, new Theme("Windows11Light"));
        mainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Dispose();
        base.OnExit(e);
    }
}
