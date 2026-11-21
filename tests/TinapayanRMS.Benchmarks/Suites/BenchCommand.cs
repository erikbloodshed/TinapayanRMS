using BenchmarkDotNet.Running;
using TinapayanRMS.Benchmarks.Scenarios;

namespace TinapayanRMS.Benchmarks.Suites;

/// <summary>
/// The bench command: the run's databases restored, the scenarios asked for timed by BenchmarkDotNet, the databases dropped
/// again whatever happens. Fails (1) when a benchmark failed or its scenario's check did.
/// </summary>
public static class BenchCommand
{
    /// <param name="options"><c>--quick</c>, <c>--scenario patterns</c> (* for anything, a comma between them), <c>--artifacts folder</c>.</param>
    public static async Task<int> RunAsync(IReadOnlyList<string> options)
    {
        var quick = false;
        string? pattern = null;
        var artifacts = Path.Combine(Path.GetTempPath(), "trms-bench", "artifacts");
        for (var i = 0; i < options.Count; i++)
        {
            switch (options[i])
            {
                case "--quick":
                    quick = true;
                    break;
                case "--scenario" when i + 1 < options.Count:
                    pattern = options[++i];
                    break;
                case "--artifacts" when i + 1 < options.Count:
                    artifacts = Path.GetFullPath(options[++i]);
                    break;
                default:
                    await Console.Error.WriteLineAsync($"Unknown option {options[i]}: bench takes --quick, --scenario <pattern> and --artifacts <folder>.");
                    return 2;
            }
        }

        BenchRun.Selected = ScenarioCatalog.Matching(pattern);
        if (BenchRun.Selected.Count == 0)
        {
            await Console.Error.WriteLineAsync($"No scenario matches {pattern}: there are {string.Join(", ", ScenarioCatalog.Names)}.");
            return 2;
        }

        List<Type> suites = [];
        if (BenchRun.SelectedWhere(needsBeforeRun: false).Any())
        {
            suites.Add(typeof(ScenarioBenchmarks));
        }

        if (BenchRun.SelectedWhere(needsBeforeRun: true).Any())
        {
            suites.Add(typeof(SteppedBenchmarks));
        }

        try
        {
            await using var databases = await BenchDatabases.CreateAsync(CancellationToken.None);
            BenchRun.Databases = databases;
            var summaries = BenchmarkRunner.Run([.. suites], BenchConfig.Create(quick, artifacts));
            var failed = summaries.Any(s => s.HasCriticalValidationErrors || s.Reports.Any(r => !r.Success) || s.Reports.Length == 0);
            return failed ? 1 : 0;
        }
        finally
        {
            AppServices.DeleteRunFolder();
        }
    }
}
