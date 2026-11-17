using System.Collections.ObjectModel;
using System.ComponentModel;
using DynamicData;
using DynamicData.Binding;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>What marks a figure worked out from a table stale (<see cref="Derived{T}"/>): any change to the table.</summary>
public static class RowChanges
{
    /// <summary>
    /// Calls <paramref name="changed"/> on any change to <paramref name="rows"/>: a row added, taken out or replaced, or
    /// any property of one changing. Broad on purpose: a figure marked stale for nothing is worked out once more, and one
    /// missed shows wrong.
    /// </summary>
    public static IDisposable OnAnyChange<TRow>(ObservableCollection<TRow> rows, Action changed)
        where TRow : INotifyPropertyChanged =>
        rows.ToObservableChangeSet()
            .AutoRefreshOnObservable(row => row.WhenAnyPropertyChanged())
            .Subscribe(_ => changed());
}
