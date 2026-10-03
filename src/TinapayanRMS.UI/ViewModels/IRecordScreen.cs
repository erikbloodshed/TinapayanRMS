using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A screen of records with a list and a form (Raw Materials, Sub-Assemblies, Products): what the Vim command line
/// (<c>:w</c>, <c>:new</c>, <c>:delete</c>, <c>:e name</c>) runs. Found by walking up from the keyboard to the first
/// DataContext that is one, as Ctrl+Z finds its screen.
/// </summary>
public interface IRecordScreen
{
    ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }

    ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    /// <summary>
    /// Opens the record <paramref name="text"/> names best (<see cref="RecordNameMatch"/>), as a link from another
    /// screen opens one. False when no record's name holds it.
    /// </summary>
    bool OpenByName(string text);
}

/// <summary>A screen whose form can throw away its changes since the last Save (Products): <c>:revert</c>.</summary>
public interface IRevertScreen
{
    ReactiveCommand<RxVoid, RxVoid> RevertCommand { get; }
}

/// <summary>
/// The record a typed name means: the one named exactly that, else one whose name starts with it, else one whose name
/// holds it, ignoring case; among equals, the first by name.
/// </summary>
public static class RecordNameMatch
{
    public static T? Best<T>(IEnumerable<T> records, Func<T, string?> nameOf, string text)
        where T : class
    {
        var wanted = text.Trim();
        if (wanted.Length == 0)
        {
            return null;
        }

        T? best = null;
        var bestRank = int.MaxValue;
        var bestName = string.Empty;
        foreach (var record in records)
        {
            var name = nameOf(record) ?? string.Empty;
            var rank = name.Equals(wanted, StringComparison.OrdinalIgnoreCase) ? 0
                : name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase) ? 1
                : name.Contains(wanted, StringComparison.OrdinalIgnoreCase) ? 2
                : -1;
            if (rank < 0)
            {
                continue;
            }

            if (rank < bestRank || (rank == bestRank && string.Compare(name, bestName, StringComparison.OrdinalIgnoreCase) < 0))
            {
                best = record;
                bestRank = rank;
                bestName = name;
            }
        }

        return best;
    }
}
