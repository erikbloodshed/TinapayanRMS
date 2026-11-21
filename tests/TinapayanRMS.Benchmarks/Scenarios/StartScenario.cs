using TinapayanRMS.Benchmarks.Startup;

namespace TinapayanRMS.Benchmarks.Scenarios;

/// <summary>
/// The app's start (AppStart) in a process that has started it before: its services built anew, the database made ready,
/// the sign-in panel loaded and signed in to, and Raw Materials loaded. Warm, so what it measures is the app's own work
/// at a start, with no JIT; the cold start, a fresh process each time, is the startup commands' (ColdStart). The last
/// run's services go before the next run, which isn't measured.
/// </summary>
public sealed class StartScenario : Scenario
{
    private UiThread? _ui;
    private string _connectionString = "";
    private AppStart? _start;

    public override string Name => "Start.SignInAndFirstScreen";

    public override bool NeedsBeforeRun => true;

    private UiThread Ui => _ui ?? throw new InvalidOperationException($"{Name} isn't set up.");

    public override Task BeforeRunAsync() => Ui.RunAsync(LeaveAsync);

    public override async Task RunAsync()
    {
        var start = AppStart.Build(_connectionString);
        _start = start;
        await start.EnsureDatabaseReadyAsync();
        await Ui.RunAsync(start.SignInAsync);
        await Ui.RunAsync(start.OpenFirstScreenAsync);
    }

    public override Task CheckAsync() => Ui.RunAsync(() =>
    {
        Screens.Ensure(_start is not null, $"{Name} wasn't run.");
        _start!.EnsureOpened();
        return Task.CompletedTask;
    });

    public override async ValueTask DisposeAsync()
    {
        if (_ui is { } ui)
        {
            await ui.RunAsync(LeaveAsync);
            ui.Dispose();
        }

        await base.DisposeAsync();
    }

    protected override Task PrepareAsync(BenchDatabases databases)
    {
        _ui = new UiThread();
        _connectionString = databases.Read;
        return Task.CompletedTask;
    }

    private async Task LeaveAsync()
    {
        if (_start is { } start)
        {
            _start = null;
            await start.DisposeAsync();
        }
    }
}
