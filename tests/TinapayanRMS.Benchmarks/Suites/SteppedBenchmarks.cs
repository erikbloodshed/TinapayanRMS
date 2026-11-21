using BenchmarkDotNet.Attributes;
using TinapayanRMS.Benchmarks.Scenarios;

namespace TinapayanRMS.Benchmarks.Suites;

/// <summary>
/// The scenarios that need something between runs that isn't measured (the cascade's material read again, a screen's last
/// page left): one run an iteration, with that before it as BenchmarkDotNet's iteration setup. Their runs take tens of
/// milliseconds or more, which one invocation an iteration times well enough.
/// </summary>
[MemoryDiagnoser]
[InvocationCount(1)]
public class SteppedBenchmarks
{
    private Scenario? _scenario;

    public static IEnumerable<string> Names => BenchRun.SelectedWhere(needsBeforeRun: true);

    [ParamsSource(nameof(Names))]
    public string Scenario { get; set; } = "";

    [GlobalSetup]
    public async Task SetUpAsync()
    {
        _scenario = ScenarioCatalog.Create(Scenario);
        await _scenario.SetUpAsync(BenchRun.Databases);
    }

    [IterationSetup]
    public Task BeforeRunAsync() => _scenario!.BeforeRunAsync();

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
