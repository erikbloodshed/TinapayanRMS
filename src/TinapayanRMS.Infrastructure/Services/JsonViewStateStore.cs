using System.Text.Json;
using TinapayanRMS.ApplicationCore.Interfaces;

namespace TinapayanRMS.Infrastructure.Services;

/// <summary>
/// IViewStateStore as one small JSON file: an object of string lists by key, read once and written whole on each write.
/// Reads and writes take a lock, since the Products list writes from a background thread once its folders settle. A file
/// that's missing, unreadable or malformed reads as empty, and a write that fails is dropped: the file only ever holds
/// conveniences, and none of them is worth an error.
/// </summary>
public sealed class JsonViewStateStore(string filePath) : IViewStateStore
{
    /// <summary>%LocalAppData%\TinapayanRMS\view-state.json: the Windows user's own, on this computer.</summary>
    public static string DefaultFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TinapayanRMS", "view-state.json");

    private readonly Lock _lock = new();
    private Dictionary<string, List<string>>? _values;

    public IReadOnlyList<string>? ReadList(string key)
    {
        lock (_lock)
        {
            return Values().TryGetValue(key, out var values) ? [.. values] : null;
        }
    }

    public void WriteList(string key, IReadOnlyList<string> values)
    {
        lock (_lock)
        {
            var all = Values();
            all[key] = [.. values];
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);

                // Written beside it and swapped in, so a write cut short leaves the old file rather than half a new one.
                var partial = filePath + ".tmp";
                File.WriteAllText(partial, JsonSerializer.Serialize(all));
                File.Move(partial, filePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Kept in memory for the rest of the run all the same.
            }
        }
    }

    private Dictionary<string, List<string>> Values()
    {
        if (_values is null)
        {
            try
            {
                _values = File.Exists(filePath)
                    ? JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(filePath)) ?? []
                    : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _values = [];
            }
        }

        return _values;
    }
}
