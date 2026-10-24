using System.Diagnostics;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// Counts the SQL commands sent from where it's started, through every await and Task.Run after it, and from nowhere
/// else: the activities Npgsql starts for its commands, kept apart per test by an AsyncLocal. A physical connection
/// being opened is an activity of its own ("CONNECT …"), and isn't a command. For a test that a read's round trips don't
/// grow with what it reads.
/// </summary>
public sealed class SqlCommandCounter : IDisposable
{
    private static readonly AsyncLocal<SqlCommandCounter?> Current = new();

    private static readonly ActivityListener Listener = Listen();

    private int _commands;

    private SqlCommandCounter()
    {
    }

    public int Commands => Volatile.Read(ref _commands);

    /// <summary>A counter for the code that runs from here on in this test's flow.</summary>
    public static SqlCommandCounter Start()
    {
        GC.KeepAlive(Listener);
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

    private static ActivityListener Listen()
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllData,
            ActivityStarted = activity =>
            {
                if (!activity.DisplayName.StartsWith("CONNECT ", StringComparison.Ordinal) && Current.Value is { } counter)
                {
                    Interlocked.Increment(ref counter._commands);
                }
            },
        };

        ActivitySource.AddActivityListener(listener);
        return listener;
    }
}
