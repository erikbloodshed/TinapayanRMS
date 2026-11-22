using Microsoft.Extensions.Hosting;
using Serilog;

namespace TinapayanRMS.Maui.Services;

/// <summary>
/// The app's hosted services (StartupTasks: the database's check and the hourly automatic backups), started and stopped
/// as a web host does, since a MAUI app doesn't run them itself. They run while the window is open.
/// </summary>
public static class BackgroundWork
{
    /// <summary>How long the window's closing waits for a backup in hand to stop.</summary>
    private static readonly TimeSpan StopWaitsFor = TimeSpan.FromSeconds(10);

    private static int _stopped;

    public static void Start(IServiceProvider services)
    {
        foreach (var service in services.GetServices<IHostedService>())
        {
            _ = StartAsync(service);
        }
    }

    public static void Stop(IServiceProvider services)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(StopWaitsFor);
        var stopping = services.GetServices<IHostedService>().Select(service => service.StopAsync(timeout.Token));
        try
        {
            // Off the UI thread, which the services never need, so the wait can't hold up what they're waiting for. It
            // can't be awaited: the window closing and Windows ending the session both need the app ended as they return.
#pragma warning disable RS0030 // The one blocking wait allowed, at most StopWaitsFor (approved 2026-11-22, ADR 0004).
            Task.Run(() => Task.WhenAll(stopping)).Wait(StopWaitsFor);
#pragma warning restore RS0030
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "The background work didn't stop cleanly");
        }

        Log.CloseAndFlush();
    }

    private static async Task StartAsync(IHostedService service)
    {
        try
        {
            await service.StartAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Service} couldn't start", service.GetType().Name);
        }
    }
}
