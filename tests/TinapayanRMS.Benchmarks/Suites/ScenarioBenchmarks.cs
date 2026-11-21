using BenchmarkDotNet.Attributes;
using TinapayanRMS.Benchmarks.Scenarios;

namespace TinapayanRMS.Benchmarks.Suites;

/// <summary>
/// The scenarios that need nothing between runs (the reads and the costing), timed as BenchmarkDotNet times best: as many
/// runs an iteration as fill it. Set up once, and checked once more at the end, so a run that went wrong partway shows.
/// </summary>
[MemoryDiagnoser]
public class ScenarioBenchmarks
{
    private Scenario? _scenario;

    public static IEnumerable<string> Names => BenchRun.SelectedWhere(needsBeforeRun: false);

    [ParamsSource(nameof(Names))]
    public string Scenario { get; set; } = "";

    [GlobalSetup]
    public async Task SetUpAsync()
    {
        _scenario = ScenarioCatalog.Create(Scenario);
        await _scenario.SetUpAsync(BenchRun.Databases);
    }

    [Benchmark]
    public Task Run() => _scenario!.RunAsync();

    [GlobalCleanup]
    public async Task CleanUpAsync()
    {
        if (_scenario is { } scenario)
        {
            try
            {
                await scenario.CheckAsync();
            }
            finally
            {
                await scenario.DisposeAsync();
            }
        }
    }
}
