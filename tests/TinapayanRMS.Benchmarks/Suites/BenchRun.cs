using TinapayanRMS.Benchmarks.Scenarios;

namespace TinapayanRMS.Benchmarks.Suites;

/// <summary>
/// What the bench command gives the benchmarks, which BenchmarkDotNet makes itself: the run's databases, and which
/// scenarios to time. Set before BenchmarkDotNet starts, and read in the same process (the in-process toolchain).
/// </summary>
public static class BenchRun
{
    private static BenchDatabases? _databases;

    /// <summary>The databases restored for the run.</summary>
    public static BenchDatabases Databases
    {
        get => _databases ?? throw new InvalidOperationException("The benchmarks run through the bench command, which restores their databases first.");
        set => _databases = value;
    }

    /// <summary>The names of the scenarios to time (--scenario), every one unless narrowed.</summary>
    public static IReadOnlyList<string> Selected { get; set; } = ScenarioCatalog.Names;

    /// <summary>The selected scenarios that need nothing between runs, or exactly those that do.</summary>
    public static IEnumerable<string> SelectedWhere(bool needsBeforeRun)
    {
        foreach (var name in Selected)
        {
            if (ScenarioCatalog.Create(name).NeedsBeforeRun == needsBeforeRun)
            {
                yield return name;
            }
        }
    }
}
