using System.Windows.Input;
using AwesomeAssertions;
using ReactiveUI;
using Serilog.Events;
using TinapayanRMS.Components.Tests.Fixtures;

namespace TinapayanRMS.Components.Tests;

/// <summary>
/// UnhandledFailures: what fails with nothing to catch it reaches the log, and a ReactiveUI pipeline's failure reaches the
/// page as well, where ReactiveUI's own handler threw it on whatever thread it failed on. Held apart from each other, since
/// what's reported is the process's.
/// </summary>
[NotInParallel(nameof(UnhandledFailures))]
public class UnhandledFailuresTests
{
    [Test]
    public void A_report_is_logged_as_an_error_and_told_to_the_page()
    {
        var log = new LogEvents();
        var told = new List<Exception>();
        void Tell(Exception exception) => told.Add(exception);
        UnhandledFailures.Reported += Tell;
        try
        {
            var failure = new InvalidOperationException("The recipe's line has no ingredient.");

            UnhandledFailures.Report(log.Logger, failure);

            log.Events.Should().ContainSingle().Which.Should().Match<LogEvent>(e => e.Level == LogEventLevel.Error && e.Exception == failure);
            told.Should().Equal(failure);
        }
        finally
        {
            UnhandledFailures.Reported -= Tell;
        }
    }

    [Test]
    public void A_page_that_fails_to_show_a_report_doesnt_throw_into_the_pipeline_that_failed()
    {
        var log = new LogEvents();
        static void Fail(Exception exception) => throw new ObjectDisposedException("NoticeHost");
        UnhandledFailures.Reported += Fail;
        try
        {
            var report = () => UnhandledFailures.Report(log.Logger, new InvalidOperationException("Broken"));

            report.Should().NotThrow();
            log.Events.Select(e => e.Level).Should().Equal(LogEventLevel.Error, LogEventLevel.Warning);
        }
        finally
        {
            UnhandledFailures.Reported -= Fail;
        }
    }

    // ReactiveUI's own handler threw what a command's body threw, with nothing subscribed to its ThrownExceptions: the
    // page's error bar on the window's thread, and the end of the process on any other.
    [Test]
    public async Task A_command_that_throws_with_nothing_to_catch_it_is_reported_rather_than_thrown()
    {
        var told = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Tell(Exception exception) => told.TrySetResult(exception);
        UnhandledFailures.Reported += Tell;
        try
        {
            var command = ReactiveCommand.Create(() => throw new InvalidOperationException("Broken command"));

            var click = () => ((ICommand)command).Execute(null);

            click.Should().NotThrow();
            (await told.Task.WaitAsync(TimeSpan.FromSeconds(10))).Message.Should().Be("Broken command");
        }
        finally
        {
            UnhandledFailures.Reported -= Tell;
        }
    }

    [Test]
    public void An_exception_that_ends_the_app_is_logged_as_fatal()
    {
        var log = new LogEvents();
        var failure = new InvalidOperationException("Broken");

        UnhandledFailures.LogUnhandled(log.Logger, failure, terminating: true);

        log.Events.Should().ContainSingle().Which.Should().Match<LogEvent>(e => e.Level == LogEventLevel.Fatal && e.Exception == failure);
    }

    [Test]
    public void A_task_nothing_awaited_is_logged_and_observed()
    {
        var log = new LogEvents();
        var failure = new AggregateException(new InvalidOperationException("Broken"));
        var args = new UnobservedTaskExceptionEventArgs(failure);

        UnhandledFailures.LogUnobserved(log.Logger, args);

        log.Events.Should().ContainSingle().Which.Should().Match<LogEvent>(e => e.Level == LogEventLevel.Warning && e.Exception == failure);
        args.Observed.Should().BeTrue("observed, it ends nothing");
    }
}
