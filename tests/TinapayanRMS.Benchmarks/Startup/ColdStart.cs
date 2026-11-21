using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Text.Json;
using Serilog;
using TinapayanRMS.Benchmarks.Scenarios;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Benchmarks.Startup;

/// <summary>
/// The app's start, cold: one sample a process, from the process starting, with the JIT, the assemblies loading and the
/// first queries all in it, as a start of the app has them; on a database StartDatabase prepared. It loads nothing a start
/// of the app doesn't (BenchServer, not BenchDatabases), so its counts are the app's.
/// </summary>
public static class ColdStart
{
    /// <summary>
    /// One cold start on <paramref name="database"/>, written to standard output as one line of JSON: each stage's
    /// milliseconds, then what the start took in methods compiled, assemblies, SQL commands, bytes and collections.
    /// </summary>
    public static async Task<int> SampleAsync(string database)
    {
        var processStart = Process.GetCurrentProcess().StartTime;
        var atMain = DateTime.Now;
        var clock = Stopwatch.StartNew();
        using var commands = new SqlCommands();

        // MauiProgram.CreateMauiApp's steps before the services, in its order, but for the log's folder.
        Syncfusion.Telemetry.Telemetry.Disable();
        NumberCulture.Apply();
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .WriteTo.File(Path.Combine(AppServices.RunFolder, "logs", "tinapayanrms-.log"), formatProvider: CultureInfo.InvariantCulture)
            .CreateLogger();
        Log.Information("A cold start of TinapayanRMS is sampled, latency mode {LatencyMode}", GCSettings.LatencyMode);

        var start = AppStart.Build(BenchServer.ConnectionStringOf(database));
        var services = clock.Elapsed;
        try
        {
            await start.EnsureDatabaseReadyAsync();
            var readiness = clock.Elapsed;

            using var ui = new UiThread();
            await ui.RunAsync(start.SignInAsync);
            var signIn = clock.Elapsed;
            await ui.RunAsync(start.OpenFirstScreenAsync);
            var firstScreen = clock.Elapsed;
            await ui.RunAsync(() =>
            {
                start.EnsureOpened();
                return Task.CompletedTask;
            });

            var runtime = atMain - processStart;
            var sample = new Dictionary<string, object>
            {
                ["runtimeMs"] = Milliseconds(runtime),
                ["servicesMs"] = Milliseconds(services),
                ["readinessMs"] = Milliseconds(readiness - services),
                ["signInMs"] = Milliseconds(signIn - readiness),
                ["firstScreenMs"] = Milliseconds(firstScreen - signIn),
                ["totalMs"] = Milliseconds(runtime + firstScreen),
                ["jitMethods"] = JitInfo.GetCompiledMethodCount(),
                ["jitMs"] = Milliseconds(JitInfo.GetCompilationTime()),
                ["assemblies"] = AppDomain.CurrentDomain.GetAssemblies().Length,
                ["sqlCommands"] = commands.Count,
                ["allocatedBytes"] = GC.GetTotalAllocatedBytes(precise: true),
                ["gen0"] = GC.CollectionCount(0),
                ["gen1"] = GC.CollectionCount(1),
                ["gen2"] = GC.CollectionCount(2),
            };
            await Console.Out.WriteLineAsync(JsonSerializer.Serialize(sample));
            return 0;
        }
        finally
        {
            await start.DisposeAsync();
            await Log.CloseAndFlushAsync();
        }
    }

    private static double Milliseconds(TimeSpan time) => Math.Round(time.TotalMilliseconds, 1);

    /// <summary>Every SQL command the process sends while it's alive (Npgsql's activities, as SqlCommandCounter counts them).</summary>
    private sealed class SqlCommands : IDisposable
    {
        private readonly ActivityListener _listener;
        private int _count;

        public SqlCommands()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "Npgsql",
                Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
                ActivityStarted = activity =>
                {
                    if (!activity.DisplayName.StartsWith("CONNECT ", StringComparison.Ordinal))
                    {
                        Interlocked.Increment(ref _count);
                    }
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public int Count => Volatile.Read(ref _count);

        public void Dispose() => _listener.Dispose();
    }
}
