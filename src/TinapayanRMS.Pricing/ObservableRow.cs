using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TinapayanRMS.Pricing;

/// <summary>
/// A row that says when it changes, for the screens' grids and bindings to follow. It's all these rows ever took from
/// ReactiveObject (2026-11-04), whose ReactiveUI a server that only reads prices has no use for, and the screens' WhenAnyValue
/// and AutoRefresh follow plain property changes just the same.
/// </summary>
public abstract class ObservableRow : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Says <paramref name="propertyName"/> changed: one worked out from another that did.</summary>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>Sets <paramref name="field"/> to <paramref name="value"/>, and says so, unless it holds that already.</summary>
    protected void RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            RaisePropertyChanged(propertyName);
        }
    }
}
