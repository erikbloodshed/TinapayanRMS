using TinapayanRMS.Benchmarks.Scenarios;
using TinapayanRMS.Benchmarks.Startup;
using TinapayanRMS.Benchmarks.Suites;

namespace TinapayanRMS.Benchmarks;

/// <summary>
/// The command line. scripts/Compare-Benchmarks.ps1 runs each side's build of it with these, so they stay as they are
/// (docs/adr/0008): a side built from an older commit is run with the same words.
/// <list type="bullet">
/// <item><c>bench [--quick] [--scenario Screens.*] [--artifacts folder]</c>: the warm benchmarks (the default).</item>
/// <item><c>startup prepare --database tinapayanrms_bench_…</c>: the database the cold starts run on.</item>
/// <item><c>startup sample --database tinapayanrms_bench_…</c>: one cold start, as a line of JSON.</item>
/// <item><c>startup drop --database tinapayanrms_bench_…</c>: that database gone, and its backups.</item>
/// <item><c>clean</c>: every benchmark database a killed run left, dropped.</item>
/// <item><c>list</c>: the scenarios' names.</item>
/// </list>
/// </summary>
public static class Commands
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args switch
            {
                ["startup", "prepare", "--database", var name] => await StartDatabase.PrepareAsync(name),
                ["startup", "sample", "--database", var name] => await ColdStart.SampleAsync(name),
                ["startup", "drop", "--database", var name] => await StartDatabase.DropAsync(name),
                ["clean"] => await StartDatabase.CleanAsync(),
                ["list"] => await ListAsync(),
                ["bench", .. var options] => await BenchCommand.RunAsync(options),
                [] => await BenchCommand.RunAsync([]),
                [var first, ..] when first.StartsWith("--", StringComparison.Ordinal) => await BenchCommand.RunAsync(args),
                _ => await UsageAsync(),
            };
        }
        catch (Exception ex)
        {
            await Console.Error.WriteLineAsync(ex.ToString());
            return 1;
        }
    }

    private static async Task<int> ListAsync()
    {
        foreach (var name in ScenarioCatalog.Names)
        {
            await Console.Out.WriteLineAsync(name);
        }

        return 0;
    }

    private static async Task<int> UsageAsync()
    {
        await Console.Error.WriteLineAsync(
            "Usage: bench [--quick] [--scenario <pattern>] [--artifacts <folder>] | startup prepare|sample|drop --database <name> | clean | list");
        return 2;
    }
}
