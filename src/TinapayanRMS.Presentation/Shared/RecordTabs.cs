using System.Collections.ObjectModel;
using System.ComponentModel;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// A record view's form, as its tab shows it: the record it holds (0 for a new one not saved yet), the name on the tab, and
/// whether it holds unsaved changes. The form tells of a change to any of them through PropertyChanged.
/// </summary>
public interface ITabForm : IUnsavedChangesScreen, INotifyPropertyChanged
{
    /// <summary>The saved record the form shows, by its id; 0 for a new one not saved yet.</summary>
    int RecordId { get; }

    /// <summary>The name on the tab: the record's as saved, or "New sub-assembly" for a new one.</summary>
    string TabTitle { get; }

    /// <summary>
    /// The view's list has saved the records <paramref name="ids"/> itself (a rename, a move), before the view loads: the
    /// form takes its record's new saved time at that load if it's one of them, keeping what's typed.
    /// </summary>
    void FollowOwnSaveOf(IEnumerable<int> ids);
}

/// <summary>
/// A view's open forms, whichever of them shows, and whether or not the view does: what the window asks about as it
/// closes (UnsavedChangesGuard), and what its leave warning follows.
/// </summary>
public interface IOpenForms
{
    IReadOnlyList<IUnsavedChangesScreen> OpenForms { get; }
}

/// <summary>
/// One tab of a record view (<see cref="RecordTabs{TForm}"/>): a form, holding a record or a new one, and whether it's the
/// view's preview tab, which the next record opened from the list replaces, as Visual Studio Code's preview editor is.
/// </summary>
public sealed class RecordTab<TForm> : ObservableState
    where TForm : class, ITabForm
{
    internal RecordTab(TForm form, int number, bool isPreview)
    {
        Form = form;
        Number = number;
        _isPreview = isPreview;
    }

    public TForm Form { get; }

    /// <summary>
    /// The tab's own number, unique in its view: a new record's address carries it (sub-assemblies/new/3), and it stays the
    /// tab's once the record is saved, so going back to that address finds the tab.
    /// </summary>
    public int Number { get; }

    private bool _isPreview;
    /// <summary>
    /// Whether this is the view's preview tab: the next record opened from the list takes its place while its form holds
    /// nothing unsaved. A double-click keeps it, as an edit does (RecordViewState).
    /// </summary>
    public bool IsPreview
    {
        get => _isPreview;
        internal set => RaiseAndSetIfChanged(ref _isPreview, value);
    }
}

/// <summary>
/// A record view's tabs, in their order along its strip, and the one showing (<see cref="Active"/>). Each holds a form of
/// its own, so each record keeps its edits, and its undo, while another shows. At most one is the preview tab. The view
/// opens, keeps and closes them (RecordViewState); <see cref="Changed"/> says whatever the strip draws has changed.
/// </summary>
public sealed class RecordTabs<TForm> : ObservableState
    where TForm : class, ITabForm
{
    private readonly ObservableCollection<RecordTab<TForm>> _all = [];
    private int _lastNumber;

    public RecordTabs()
    {
        All = new ReadOnlyObservableCollection<RecordTab<TForm>>(_all);
    }

    /// <summary>The tabs, in their order along the strip.</summary>
    public ReadOnlyObservableCollection<RecordTab<TForm>> All { get; }

    private RecordTab<TForm>? _active;
    /// <summary>The tab showing; null while none is open.</summary>
    public RecordTab<TForm>? Active
    {
        get => _active;
        private set => RaiseAndSetIfChanged(ref _active, value);
    }

    /// <summary>The preview tab, if there is one.</summary>
    public RecordTab<TForm>? Preview => _all.FirstOrDefault(tab => tab.IsPreview);

    /// <summary>
    /// Raised when anything the strip draws changes: a tab opened, closed, shown or kept, or a form's title, record or unsaved
    /// changes.
    /// </summary>
    public event Action? Changed;

    /// <summary>The tab holding the saved record <paramref name="recordId"/>; null for none, or for 0.</summary>
    public RecordTab<TForm>? Holding(int recordId) => recordId > 0 ? _all.FirstOrDefault(tab => tab.Form.RecordId == recordId) : null;

    /// <summary>The tab numbered <paramref name="number"/> (<see cref="RecordTab{TForm}.Number"/>), if it's still open.</summary>
    public RecordTab<TForm>? Numbered(int number) => _all.FirstOrDefault(tab => tab.Number == number);

    /// <summary>A tab for <paramref name="form"/>, after the one showing (or last, with none), not shown yet.</summary>
    internal RecordTab<TForm> Add(TForm form, bool isPreview)
    {
        var tab = new RecordTab<TForm>(form, ++_lastNumber, isPreview);
        _all.Insert(Active is { } active ? _all.IndexOf(active) + 1 : _all.Count, tab);
        tab.PropertyChanged += OnTabChanged;
        form.PropertyChanged += OnFormChanged;
        Changed?.Invoke();
        return tab;
    }

    /// <summary>Shows <paramref name="tab"/>, or none.</summary>
    internal void Activate(RecordTab<TForm>? tab)
    {
        if (tab != Active)
        {
            Active = tab;
            Changed?.Invoke();
        }
    }

    /// <summary>Takes <paramref name="tab"/> off the strip. The one showing gives way to the tab before it, or else the one after.</summary>
    internal void Remove(RecordTab<TForm> tab)
    {
        var at = _all.IndexOf(tab);
        if (at < 0)
        {
            return;
        }

        tab.PropertyChanged -= OnTabChanged;
        tab.Form.PropertyChanged -= OnFormChanged;
        _all.RemoveAt(at);
        if (tab == Active)
        {
            Active = _all.Count == 0 ? null : _all[Math.Max(at - 1, 0)];
        }

        Changed?.Invoke();
    }

    private void OnTabChanged(object? sender, PropertyChangedEventArgs e) => Changed?.Invoke();

    private void OnFormChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ITabForm.TabTitle) or nameof(ITabForm.RecordId) or nameof(IUnsavedChangesScreen.HasUnsavedChanges))
        {
            Changed?.Invoke();
        }
    }
}
