using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using Perfolizer.Horology;

namespace TinapayanRMS.Benchmarks.Suites;

/// <summary>
/// How the benchmarks are run: in this process (the in-process toolchain), which shares the one restore of the run's
/// databases and needs no child build of its own; the comparison's alternating processes keep two builds apart instead
/// (scripts/Compare-Benchmarks.ps1). Each benchmark's full results go to JSON, which the comparison reads.
/// </summary>
public static class BenchConfig
{
    public static IConfig Create(bool quick, string artifactsFolder)
    {
        var job = quick
            ? Job.ShortRun.WithId("Quick")
            : Job.Default.WithId("Bench").WithWarmupCount(2).WithIterationCount(10).WithIterationTime(TimeInterval.FromMilliseconds(250));

        // A scenario's whole name in the reports (BenchmarkDotNet shortens a parameter past 20 characters), which the
        // comparison knows it by.
        return ManualConfig.Create(DefaultConfig.Instance)
            .AddJob(job.WithToolchain(InProcessEmitToolchain.Instance))
            .AddExporter(JsonExporter.Full)
            .WithArtifactsPath(artifactsFolder)
            .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(80))
            .WithOptions(ConfigOptions.JoinSummary);
    }
}
