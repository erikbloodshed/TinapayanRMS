using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A row that says when it changes, for the screens' grids and bindings to follow: a lighter base than ReactiveObject for
/// the editable rows, which need nothing else from it. WhenAnyValue and AutoRefresh follow plain property changes.
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
