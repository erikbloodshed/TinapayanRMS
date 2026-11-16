using System.Windows.Input;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A screen of records with a list and a form (Raw Materials, Sub-Assemblies, Products): what the Vim command line
/// (<c>:w</c>, <c>:new</c>, <c>:delete</c>, <c>:e name</c>) runs, on the screen
/// showing (VimKeysState, which each screen tells as it shows and goes).
/// </summary>
public interface IRecordScreen
{
    ICommand NewCommand { get; }

    ICommand SaveCommand { get; }

    ICommand DeleteCommand { get; }

    /// <summary>
    /// Opens the record <paramref name="text"/> names best (<see cref="RecordNameMatch"/>), as a link from another
    /// screen opens one. False when no record's name holds it.
    /// </summary>
    bool OpenByName(string text);
}

/// <summary>
/// A screen of records with an address each (products/42): what its page (RecordScreenBase) opens the record in the address
/// through. The three record screens, through RecordFormViewModel.
/// </summary>
public interface IRecordOpener
{
    /// <summary>Opens the record now, as a pick in the list does: Back, Forward, or another screen's link.</summary>
    void OpenRecord(int id);

    /// <summary>Opens the record at the end of the load the screen's opening starts: the list is fresh only then.</summary>
    void OpenRecordOnLoad(int id);
}

/// <summary>A screen whose form can throw away its changes since the last Save (Products): <c>:revert</c>.</summary>
public interface IRevertScreen
{
    ICommand RevertCommand { get; }
}

/// <summary>
/// What a typed name means, ignoring case: the record named exactly that, else one whose name starts with it, else one
/// with a word in its name starting with it ("salt" finds Sea Salt before Unsalted Butter), else one whose name holds it.
/// </summary>
public static class RecordNameMatch
{
    /// <summary>The one record the text names best; among equals, the first by name. None when nothing's typed.</summary>
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
            var rank = RankOf(name, wanted);
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

    /// <summary>
    /// How well a name matches typed text, lower being better (a picker's list is sorted by it): 0 the exact name,
    /// 1 starting with it, 2 a word starting with it, 3 holding it; none when it doesn't hold it, or nothing's typed.
    /// </summary>
    public static int? Rank(string? name, string text)
    {
        var wanted = text.Trim();
        return wanted.Length == 0 || RankOf(name ?? string.Empty, wanted) is not (>= 0 and var rank) ? null : rank;
    }

    private static int RankOf(string name, string wanted)
    {
        if (name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        if (name.StartsWith(wanted, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        // It can be found inside a word before it's found at one's start ("Unsalted Salt"), so each place is checked.
        var at = name.IndexOf(wanted, StringComparison.OrdinalIgnoreCase);
        if (at < 0)
        {
            return -1;
        }

        for (; at >= 0; at = name.IndexOf(wanted, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            if (!char.IsLetterOrDigit(name[at - 1]))
            {
                return 2;
            }
        }

        return 3;
    }
}
