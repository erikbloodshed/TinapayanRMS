using System.Collections.Concurrent;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>
/// IViewStateStore kept in memory, for a screen state under test: it starts with what it's given, and keeps what's written
/// for the test to read back. Safe to write from the thread pool, as the Products list's throttled writer does.
/// </summary>
public sealed class InMemoryViewStateStore(IReadOnlyDictionary<string, IReadOnlyList<string>>? values = null) : IViewStateStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _values = new(values ?? new Dictionary<string, IReadOnlyList<string>>());

    /// <summary>How many writes have been made, the same values or not.</summary>
    public int WriteCount { get; private set; }

    public IReadOnlyList<string>? ReadList(string key) => _values.GetValueOrDefault(key);

    public void WriteList(string key, IReadOnlyList<string> values)
    {
        _values[key] = [.. values];
        WriteCount++;
    }
}
