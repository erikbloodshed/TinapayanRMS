using System.Collections.Concurrent;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>IViewStateStore kept in memory, in place of the Windows user's file: it starts with what it's given and keeps what's written.</summary>
public sealed class InMemoryViewStateStore(IReadOnlyDictionary<string, IReadOnlyList<string>>? values = null) : IViewStateStore
{
    private readonly ConcurrentDictionary<string, IReadOnlyList<string>> _values = new(values ?? new Dictionary<string, IReadOnlyList<string>>());

    public IReadOnlyList<string>? ReadList(string key) => _values.GetValueOrDefault(key);

    public void WriteList(string key, IReadOnlyList<string> values) => _values[key] = [.. values];
}
