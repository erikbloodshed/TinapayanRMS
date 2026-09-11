using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TinapayanRMS.Infrastructure;

namespace TinapayanRMS.UI;

/// <summary>
/// Builds the app's DI host. Split out from App.xaml.cs so tests can build and
/// resolve from it without starting the WPF application.
/// </summary>
public static class CompositionRoot
{
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        ReactiveUIBootstrapper.EnsureInitialized();

        return new HostBuilder()
            .ConfigureAppConfiguration(config =>
            {
                config.SetBasePath(AppContext.BaseDirectory);
                config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: false);
            })
            .ConfigureServices((context, services) =>
            {
                var connectionString = context.Configuration.GetConnectionString("Default")
                    ?? throw new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");

                services.AddDbContext<TinapayanRMSDbContext>(options => options.UseSqlServer(connectionString));

                services.AddSingleton<MainWindowViewModel>();
                services.AddSingleton<MainWindow>();
            });
    }
}
