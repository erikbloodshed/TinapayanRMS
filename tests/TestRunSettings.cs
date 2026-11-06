namespace TinapayanRMS.Tests;

/// <summary>
/// What every test project runs with: tests/Directory.Build.props compiles this file into each, since a TUnit hook is
/// only found in the tests' own assembly.
/// </summary>
public static class TestRunSettings
{
    /// <summary>
    /// <list type="bullet">
    /// <item>At most as many tests at once as the PC has processors, where TUnit would run twice that. The suite's time is
    /// its chains of tests that run one at a time (the backup tests, the off-screen grids), and the extra tests beside them
    /// slowed them down: the whole suite took 5m43s to 6m35s at TUnit's 24 on this 12-processor PC, and 4m29s to 4m50s
    /// at 12. --maximum-parallel-tests or TUNIT_MAX_PARALLEL_TESTS overrides it.</item>
    /// <item>No HTML report or its JSON beside it. TUnit writes both to TestResults on every run unless told not to,
    /// 20 MB for Infrastructure.Tests alone, and records every traced call for them while the tests run, each SQL
    /// command Npgsql sends included. For one, set them back to true here (--report-html no longer turns it on).</item>
    /// </list>
    /// </summary>
    [Before(TestDiscovery)]
    public static void Configure(BeforeTestDiscoveryContext context)
    {
        context.Settings.Parallelism.MaximumParallelTests = Environment.ProcessorCount;
        context.Settings.Reporting.HtmlReportEnabled = false;
        context.Settings.Reporting.JsonReportEnabled = false;
    }
}
