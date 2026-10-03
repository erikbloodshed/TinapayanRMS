using System.Diagnostics;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// Counts the SQL commands sent from where it's started, through every await and Task.Run after it, and from nowhere
/// else: what SqlClient reports to its DiagnosticListener, kept apart per test by an AsyncLocal. For a test that a read's
/// round trips don't grow with what it reads.
/// </summary>
public sealed class SqlCommandCounter : IDisposable
{
    private static readonly AsyncLocal<SqlCommandCounter?> Current = new();

    private static readonly IDisposable Listening = DiagnosticListener.AllListeners.Subscribe(new ListenerObserver());

    private int _commands;

    private SqlCommandCounter()
    {
    }

    public int Commands => Volatile.Read(ref _commands);

    /// <summary>A counter for the code that runs from here on in this test's flow.</summary>
    public static SqlCommandCounter Start()
    {
        GC.KeepAlive(Listening);
        var counter = new SqlCommandCounter();
        Current.Value = counter;
        return counter;
    }

    public void Dispose()
    {
        if (ReferenceEquals(Current.Value, this))
        {
            Current.Value = null;
        }
    }

    private sealed class ListenerObserver : IObserver<DiagnosticListener>
    {
        public void OnNext(DiagnosticListener value)
        {
            if (value.Name == "SqlClientDiagnosticListener")
            {
                value.Subscribe(new EventObserver());
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }

    private sealed class EventObserver : IObserver<KeyValuePair<string, object?>>
    {
        public void OnNext(KeyValuePair<string, object?> value)
        {
            if (value.Key.EndsWith("WriteCommandBefore", StringComparison.Ordinal) && Current.Value is { } counter)
            {
                Interlocked.Increment(ref counter._commands);
            }
        }

        public void OnCompleted()
        {
        }

        public void OnError(Exception error)
        {
        }
    }
}
