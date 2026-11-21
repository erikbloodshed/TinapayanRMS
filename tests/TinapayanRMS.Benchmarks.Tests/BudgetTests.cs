using AwesomeAssertions;
using TinapayanRMS.Benchmarks.Scenarios;
using TinapayanRMS.Benchmarks.Tests.Budgets;
using TinapayanRMS.Benchmarks.Tests.Fixtures;
using TinapayanRMS.Tests.Common.Fixtures;

namespace TinapayanRMS.Benchmarks.Tests;

/// <summary>
/// Each of the benchmarks' scenarios (ScenarioCatalog) held to its budget (Budgets/budgets.approved.txt): the SQL commands
/// it sends exactly, as ADR 0004's ratchets hold a count (more fails, and so does fewer, until the lower one is approved),
/// and the memory it allocates within 10% over and 20% under. Counts, not times, so they hold in the suite whatever else
/// the PC is doing (docs/adr/0008). A change meant to move one approves the new figures: see ApprovedBudgets.ApproveVariable.
/// One at a time, and alone in its process, so nothing else allocates while a scenario is measured.
/// </summary>
[NotInParallel(nameof(BudgetTests))]
[ClassDataSource<BudgetDatabases>(Shared = SharedType.PerClass)]
public sealed class BudgetTests(BudgetDatabases databases)
{
    /// <summary>How far over its approved allocations a scenario may go: the measure's own noise is a few per cent.</summary>
    private const decimal AllocationsOver = 1.10m;

    /// <summary>How far under: a scenario that allocates this much less is approved again, so its budget stays near it.</summary>
    private const decimal AllocationsUnder = 0.80m;

    private static readonly string HowToApprove =
        $"if the change is meant to move it, set {ApprovedBudgets.ApproveVariable}=1, run Benchmarks.Tests, and read the diff of Budgets/{ApprovedBudgets.FileName} before keeping it";

    public static IEnumerable<string> Scenarios() => ScenarioCatalog.Names;

    [Test]
    [MethodDataSource(nameof(Scenarios))]
    public async Task A_scenario_takes_the_queries_and_memory_approved(string scenario)
    {
        var measured = await MeasureAsync(scenario);
        if (ApprovedBudgets.Approving)
        {
            Environment.GetEnvironmentVariable("CI").Should().BeNullOrEmpty("budgets are approved on a developer's PC, where the change can be read");
            ApprovedBudgets.Approve(measured);
            return;
        }

        ApprovedBudgets.Read().TryGetValue(scenario, out var approved).Should().BeTrue($"{scenario} should have a budget: {HowToApprove}");
        measured.Commands.Should().Be(
            approved!.Commands,
            $"{scenario} should send the {approved.Commands} SQL commands approved (it sent {measured.Commands}): {HowToApprove}");
        measured.Kilobytes.Should().BeLessThanOrEqualTo(
            (long)(approved.Kilobytes * AllocationsOver),
            $"{scenario} should allocate at most 10% over the {approved.Kilobytes} KB approved (it allocated {measured.Kilobytes} KB): {HowToApprove}");
        measured.Kilobytes.Should().BeGreaterThanOrEqualTo(
            (long)(approved.Kilobytes * AllocationsUnder),
            $"{scenario} allocates {measured.Kilobytes} KB, more than 20% under the {approved.Kilobytes} KB approved: approve the lower figure so its budget holds it ({ApprovedBudgets.ApproveVariable}=1)");
    }

    [Test]
    public void Every_budget_approved_is_a_scenarios()
    {
        if (ApprovedBudgets.Approving)
        {
            ApprovedBudgets.DropOrphans();
            return;
        }

        ApprovedBudgets.Read().Keys.Except(ScenarioCatalog.Names).Should().BeEmpty(
            $"every budget should be a scenario's: one renamed or taken out of ScenarioCatalog goes from Budgets/{ApprovedBudgets.FileName} too ({ApprovedBudgets.ApproveVariable}=1 drops it)");
    }

    /// <summary>
    /// What a run of <paramref name="name"/> takes once it's warm: the SQL commands it sends, counted until none has come for
    /// a while (a screen's background reads included), and the least it allocates in three runs, since what else runs in
    /// the process (the pools' timers) only ever adds to it.
    /// </summary>
    private async Task<Budget> MeasureAsync(string name)
    {
        await using var scenario = ScenarioCatalog.Create(name);
        await scenario.SetUpAsync(databases.Databases);
        for (var warm = 0; warm < 2; warm++)
        {
            await scenario.BeforeRunAsync();
            await scenario.RunAsync();
        }

        await scenario.CheckAsync();

        int commands;
        await scenario.BeforeRunAsync();
        using (var counter = SqlCommandCounter.Start())
        {
            await scenario.RunAsync();
            commands = await counter.SettledAsync();
        }

        await scenario.CheckAsync();

        var least = long.MaxValue;
        for (var run = 0; run < 3; run++)
        {
            await scenario.BeforeRunAsync();
            var before = GC.GetTotalAllocatedBytes(precise: true);
            await scenario.RunAsync();
            least = Math.Min(least, GC.GetTotalAllocatedBytes(precise: true) - before);
        }

        await scenario.CheckAsync();
        return new Budget(name, commands, (least + 512) / 1024);
    }
}
