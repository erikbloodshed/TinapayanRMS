using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// Something a screen shows that says when it changes, without ReactiveUI: a part of a screen's ViewModel (Raw Materials'
/// Cost Setup tab, say), or an editable row. A screen draws itself again whenever its ViewModel or one of its parts says
/// anything changed (ScreenBase), and reads every value then, so only what's stored says so: a value worked out from others
/// is a plain getter, and what follows from a change is called by the setter that made it, in the order it has to run.
/// </summary>
public abstract class ObservableState : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Says <paramref name="propertyName"/> changed: one worked out from another that did.</summary>
    protected void RaisePropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        OnPropertyChanged(propertyName);
    }

    /// <summary>
    /// What follows any change said of <paramref name="propertyName"/>, however it was said: for a part whose properties are
    /// set from several places, the quiet ones too (a load's, an undo's), each of which has to set the same thing off.
    /// Nothing, unless a part says otherwise.
    /// </summary>
    protected virtual void OnPropertyChanged(string? propertyName)
    {
    }

    /// <summary>
    /// Sets <paramref name="field"/> to <paramref name="value"/>, and says so, unless it holds that already. True when it
    /// changed, for a setter with more to do then.
    /// </summary>
    protected bool RaiseAndSetIfChanged<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        RaisePropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// A row that says when it changes, for the screens' grids and bindings to follow: a lighter base than ReactiveObject for
/// the editable rows, which need nothing else from it. WhenAnyValue and AutoRefresh follow plain property changes.
/// </summary>
public abstract class ObservableRow : ObservableState;
