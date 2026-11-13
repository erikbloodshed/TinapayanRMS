using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>A logger for a test, which keeps what's logged to it, for the test to read.</summary>
public sealed class LogEvents : ILogEventSink
{
    private readonly List<LogEvent> _events = [];

    public LogEvents()
    {
        Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(this).CreateLogger();
    }

    public Serilog.ILogger Logger { get; }

    /// <summary>What was logged, in order.</summary>
    public IReadOnlyList<LogEvent> Events
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public void Emit(LogEvent logEvent)
    {
        lock (_events)
        {
            _events.Add(logEvent);
        }
    }
}
