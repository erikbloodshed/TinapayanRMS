using AwesomeAssertions;
using Serilog;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Presentation.Tests.Fixtures;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.Presentation.Tests.ViewModels;

/// <summary>
/// The database made ready once per start: one run however many ask (the sign-in panel and the startup tasks), saying
/// while it applies updates, and run again after one that failed.
/// </summary>
public class DatabaseReadinessTests
{
    /// <summary>An updater a test lets finish, applying <see cref="Updates"/> updates, or failing while <see cref="Fail"/> is set.</summary>
    private sealed class HeldUpdater : IDatabaseUpdater
    {
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Runs { get; private set; }

        public int Updates { get; init; }

        public bool Fail { get; set; }

        public async Task<DatabaseUpdate> UpdateAsync(Action<int>? applying, CancellationToken cancellationToken)
        {
            if (Updates > 0)
            {
                applying?.Invoke(Updates);
            }

            // Counted once it has said it's applying, so a test that sees the run sees that too.
            Runs++;

            await Release.Task;
            return Fail ? throw new BusinessRuleException(BackupRules.DatabaseFromLaterVersionText) : new DatabaseUpdate(Updates, null, Created: false);
        }
    }

    private static DatabaseReadiness Readiness(IDatabaseUpdater updater) => new(updater, new LoggerConfiguration().CreateLogger());

    [Test]
    public async Task Everyone_who_asks_waits_for_the_one_run()
    {
        var updater = new HeldUpdater();
        var readiness = Readiness(updater);

        var first = readiness.EnsureReadyAsync();
        var second = readiness.EnsureReadyAsync();
        updater.Release.SetResult();
        await Task.WhenAll(first, second);
        await readiness.EnsureReadyAsync();

        updater.Runs.Should().Be(1);
    }

    [Test]
    public async Task It_says_its_updating_only_while_updates_are_applied()
    {
        var updater = new HeldUpdater { Updates = 2 };
        var readiness = Readiness(updater);
        var changes = new List<bool>();
        readiness.Changed += () => changes.Add(readiness.IsUpdating);

        var ready = readiness.EnsureReadyAsync();
        await Settle.EventuallyAsync(() => updater.Runs == 1);
        readiness.IsUpdating.Should().BeTrue();
        updater.Release.SetResult();
        await ready;

        readiness.IsUpdating.Should().BeFalse();
        changes.Should().Equal(true, false);
    }

    [Test]
    public async Task A_database_already_up_to_date_never_says_its_updating()
    {
        var updater = new HeldUpdater();
        var readiness = Readiness(updater);
        var changes = 0;
        readiness.Changed += () => changes++;

        updater.Release.SetResult();
        await readiness.EnsureReadyAsync();

        changes.Should().Be(0);
    }

    [Test]
    public async Task A_run_that_failed_is_run_again_by_the_next_ask()
    {
        var updater = new HeldUpdater { Fail = true };
        updater.Release.SetResult();
        var readiness = Readiness(updater);

        var failed = () => readiness.EnsureReadyAsync();
        await failed.Should().ThrowAsync<BusinessRuleException>().WithMessage(BackupRules.DatabaseFromLaterVersionText);

        updater.Fail = false;
        await readiness.EnsureReadyAsync();

        updater.Runs.Should().Be(2);
        readiness.IsUpdating.Should().BeFalse();
    }
}
