using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A table's rows, replaced all together as one change (<see cref="ReplaceWith"/>): a single Reset, where Clear and an
/// Add for each row are a change each. A grid drawn over the list (Syncfusion's SfGrid) refreshes and draws every row
/// again for each change it hears of, so Reports' tables, filled a row at a time, were drawn some 330 times a visit,
/// each time with more rows, and held the window for 7 seconds of its 9 (2026-11-07). DynamicData's AddRange adds a row
/// at a time to an ObservableCollection, and so does ObservableCollectionExtended's.
/// </summary>
public sealed class TableRows<T> : ObservableCollection<T>
{
    /// <summary>
    /// Makes the table hold <paramref name="rows"/>, in their order, and says so once. Rows the same as those it holds,
    /// value for value and in order, change nothing: it keeps its own and says nothing, since the grid would draw every
    /// row again for the same figures, as most of Reports' visits found them.
    /// </summary>
    /// <returns>Whether the table changed.</returns>
    public bool ReplaceWith(IEnumerable<T> rows)
    {
        CheckReentrancy();

        // Read first: the rows can be worked out from the table itself (only those below target, say).
        List<T> fresh = [.. rows];
        if (fresh.Count == Count && fresh.Select((row, i) => IsSameRow(row, Items[i])).All(same => same))
        {
            return false;
        }

        Items.Clear();
        foreach (var row in fresh)
        {
            Items.Add(row);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return true;
    }

    // A value or a record is the same by its own Equals; a row class by the values it shows, its public scalar properties
    // (CollectionMerge), since each load makes new ones. Not a string's: CollectionMerge would compare its Length alone.
    private static bool IsSameRow(T fresh, T held) =>
        EqualityComparer<T>.Default.Equals(fresh, held)
        || (!typeof(T).IsValueType && typeof(T) != typeof(string) && CollectionMerge.HasSameValues(fresh, held));
}
