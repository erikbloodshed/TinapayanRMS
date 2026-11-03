using System.IO;
using AwesomeAssertions;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace TinapayanRMS.UI.Tests;

/// <summary>
/// What the app does when it can't start. It used to reach App's handler for a running app, which said the app was still
/// running and left the process alive with no window to close it from.
/// </summary>
public class AppStartupTests
{
    private const string LogFolder = @"C:\Apps\TinapayanRMS\logs";

    private readonly List<string> _said = [];
    private readonly List<int> _exitCodes = [];
    private readonly List<LogEvent> _logged = [];

    [Fact]
    public void A_start_that_goes_through_says_nothing_and_stays_open()
    {
        var started = false;

        var result = TryStart(() => started = true);

        result.Should().BeTrue();
        started.Should().BeTrue();
        _said.Should().BeEmpty();
        _exitCodes.Should().BeEmpty();
        _logged.Should().BeEmpty();
    }

    [Fact]
    public void A_start_that_fails_says_why_and_closes_the_app()
    {
        var cause = new FileNotFoundException("The configuration file 'appsettings.json' was not found and is not optional.");

        var result = TryStart(() => throw cause);

        result.Should().BeFalse();
        _exitCodes.Should().Equal([AppStartup.FailedExitCode], "with no window there'd be nothing to close the process from");
        var said = _said.Should().ContainSingle().Subject;
        said.Should().Contain("couldn't start").And.Contain("appsettings.json").And.Contain(LogFolder);
        said.Should().NotContain("still running").And.NotContain("Excel");
    }

    [Fact]
    public void A_start_that_fails_is_logged_with_its_exception_before_anything_is_shown()
    {
        var cause = new InvalidOperationException("Missing 'ConnectionStrings:Default' configuration value.");
        var loggedBeforeSaying = 0;

        AppStartup.TryStart(
            () => throw cause,
            LogFolder,
            CreateLogger(),
            _ => loggedBeforeSaying = _logged.Count,
            _exitCodes.Add);

        loggedBeforeSaying.Should().Be(1, "the message waits on the user, and the log mustn't");
        var logged = _logged.Should().ContainSingle().Subject;
        logged.Level.Should().Be(LogEventLevel.Fatal);
        logged.Exception.Should().BeSameAs(cause);
    }

    [Fact]
    public void Only_the_first_line_of_the_failure_is_shown()
    {
        var said = AppStartup.FailureText(new InvalidOperationException("The cause." + Environment.NewLine + "   at Somewhere()"), LogFolder);

        said.Should().Contain("The cause.").And.NotContain("Somewhere");
    }

    private bool TryStart(Action start) => AppStartup.TryStart(start, LogFolder, CreateLogger(), _said.Add, _exitCodes.Add);

    private Logger CreateLogger() => new LoggerConfiguration().WriteTo.Sink(new ListSink(_logged)).CreateLogger();

    private sealed class ListSink(List<LogEvent> events) : ILogEventSink
    {
        public void Emit(LogEvent logEvent) => events.Add(logEvent);
    }
}
