using System.Reactive.Disposables;
using TinapayanRMS.ApplicationCore.Rules;
using TinapayanRMS.Presentation.Trees;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// A record view (Sub-Assemblies, Raw Materials, Products): its list beside a strip of tabs, each holding a form of its own
/// (<see cref="Tabs"/>), as Visual Studio Code's editors are, so each record keeps its edits and its undo while another
/// shows, and a record shows in one tab at most. A record picked in the list opens in the preview tab, which the next pick
/// replaces while it holds nothing unsaved; one with changes, or kept by a double-click, stays. The view's own work (its
/// load, the list's actions) runs as the form showing does (<see cref="Runner"/>), so what it says shows on that form; and a
/// form's question asked while no page shows it goes to the view's own page (<see cref="AddTab"/>).
/// <para>
/// What the view's list and Vim keys ask of it is the form showing's, passed on here for every view: its part host
/// (IScreenPartHost, IFormPartHost), its undo (IUndoRecorder), its Save and Delete (IRecordScreen), and what the list's
/// actions need of the forms (IRecordListHost). Each view adds its lists, its load and its own actions.
/// </para>
/// </summary>
/// <typeparam name="TForm">The view's form: one record's, a new one's, or a copy's.</typeparam>
public abstract class RecordViewState<TForm> : ScreenState, IOpenForms, IRecordScreen, IRecordOpener, IRecordListHost, IDisposable
    where TForm : ScreenState, ITabForm, IDisposable
{
    /// <summary>What ties each tab's form to the view, let go of as the tab closes.</summary>
    private readonly Dictionary<RecordTab<TForm>, IDisposable> _wiring = [];

    /// <summary>What Save and Delete are while no tab is open (the Vim keys' <c>:w</c> and <c>:delete</c>): nothing to do.</summary>
    private readonly ScreenCommand _nothing = ScreenCommand.Of(() => { }, () => false);

    protected RecordViewState(ILogger logger)
        : base(logger)
    {
        // The list's row follows the tab showing, and a new record's row being named in the list goes with its tab.
        Tabs.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RecordTabs<>.Active))
            {
                ViewList?.EndNewRow();
                ViewList?.SyncTreeSelection();
                RaisePropertyChanged(nameof(Form));
            }
        };
    }

    /// <summary>The view's tabs, and the one showing.</summary>
    public RecordTabs<TForm> Tabs { get; } = new();

    /// <summary>The form of the tab showing; null while no tab is open.</summary>
    public TForm? Form => Tabs.Active?.Form;

    /// <summary>Reads the view's lists again and merges them into the list and every open form: as the view shows, and after a save.</summary>
    public abstract ScreenCommand LoadCommand { get; }

    /// <summary>New, from a form's header, the list or the Vim keys' <c>:new</c>: a new record in the preview tab.</summary>
    public abstract ScreenCommand NewCommand { get; }

    /// <summary>The Vim command line's <c>:e name</c>: opens the record the name means best, as an address would.</summary>
    public abstract bool OpenByName(string text);

    /// <summary>
    /// IRecordOpener: the record in the view's address (Back, Forward, a link from another screen, Find), shown as a pick in
    /// the list shows it. A list that doesn't hold it yet opens it at the end of the next load (<see cref="RecordToOpen"/>).
    /// </summary>
    public abstract void OpenRecord(int id);

    /// <summary>IRecordOpener: the record in the address the view opens on, at the end of its load, the list fresh only then.</summary>
    public virtual void OpenRecordOnLoad(int id) => RecordToOpen = id;

    /// <summary>An address is waiting for the view's load to open its record: what the view's address says meanwhile stays.</summary>
    public virtual bool IsOpeningRecord => RecordToOpen is not null;

    /// <summary>A record an address asked for before the list held it: the end of the next load opens it (<see cref="OpenRecordWaiting"/>).</summary>
    protected int? RecordToOpen { get; set; }

    /// <summary>
    /// The screen the view's own work runs as: the form showing, so what it says (a load that failed, a rename refused) shows
    /// where the user is looking, and its work counts with that form's; the view itself while no tab is open.
    /// </summary>
    protected ScreenState Runner => (ScreenState?)Form ?? this;

    /// <summary>The view's list, whose row follows the tab showing; none for a view without one.</summary>
    protected virtual RecordList? ViewList => null;

    /// <summary>A form for a new tab, tied to the view, holding nothing yet: a blank new record.</summary>
    protected abstract TForm NewForm();

    /// <summary>What else ties <paramref name="form"/> to the view (its requests to other screens), let go of as its tab closes.</summary>
    protected virtual IDisposable Wire(TForm form) => Disposable.Empty;

    /// <summary>A tab with a new form, after the one showing, not shown yet.</summary>
    protected RecordTab<TForm> AddTab(bool isPreview)
    {
        var form = NewForm();
        var tab = Tabs.Add(form, isPreview);

        // Asked after the form's own page, which answers while it shows the form: a question the form asks while it isn't
        // showing (its tab closed with changes not saved) goes to the view's page, which shows the list beside it.
        _wiring[tab] = new CompositeDisposable(
            form.ConfirmDelete.RegisterHandler(async question => question.SetOutput(await ConfirmDelete.Handle(question.Input))),
            form.ConfirmUnsavedChanges.RegisterHandler(async question => question.SetOutput(await ConfirmUnsavedChanges.Handle(question.Input))),
            Wire(form));
        return tab;
    }

    /// <summary>
    /// The tab to open a record in that has no tab of its own: the preview tab while it holds nothing unsaved, else (keeping
    /// that one, now it holds changes) a new preview tab.
    /// </summary>
    protected RecordTab<TForm> PreviewSlot()
    {
        if (Tabs.Preview is { } preview)
        {
            if (!preview.Form.HasUnsavedChanges)
            {
                return preview;
            }

            preview.IsPreview = false;
        }

        return AddTab(isPreview: true);
    }

    /// <summary>
    /// Shows the saved record <paramref name="recordId"/>: its own tab, if it has one (kept, with <paramref name="keep"/>), or
    /// else the preview slot (<see cref="PreviewSlot"/>), or a kept tab of its own with <paramref name="keep"/>, which
    /// <paramref name="load"/> fills. Returns the form that shows it.
    /// </summary>
    protected TForm OpenInTab(int recordId, bool keep, Action<TForm> load)
    {
        if (Tabs.Holding(recordId) is { } open)
        {
            if (keep)
            {
                open.IsPreview = false;
            }

            Tabs.Activate(open);
            return open.Form;
        }

        var tab = keep ? AddTab(isPreview: false) : PreviewSlot();
        load(tab.Form);
        Tabs.Activate(tab);
        return tab.Form;
    }

    /// <summary>
    /// New, or a copy: a fresh form in the preview tab, which <paramref name="start"/> fills, and shown. A record still
    /// waiting for the load to open it no longer is: New is the later word.
    /// </summary>
    protected void OpenDraft(Action<TForm> start)
    {
        RecordToOpen = null;
        var tab = PreviewSlot();
        start(tab.Form);
        Show(tab);
    }

    /// <summary>The record an address asked for before the list held it, at the end of the load: the list holds it now, or never will.</summary>
    protected void OpenRecordWaiting()
    {
        if (RecordToOpen is { } id)
        {
            RecordToOpen = null;
            OpenRecord(id);
        }
    }

    /// <summary>Shows <paramref name="tab"/>: a click on it, its address (Back, Forward), or another tab closed.</summary>
    public void Show(RecordTab<TForm> tab) => Tabs.Activate(tab);

    /// <summary>Keeps <paramref name="tab"/> open: a double-click on it, or on its record in the list. It's no longer the preview tab.</summary>
    public void Keep(RecordTab<TForm> tab) => tab.IsPreview = false;

    /// <summary>Shows the tab numbered <paramref name="number"/>, the one a new record's address names (sub-assemblies/new/3), if it's still open.</summary>
    public void ShowNumbered(int number)
    {
        if (Tabs.Numbered(number) is { } tab)
        {
            Tabs.Activate(tab);
        }
    }

    /// <summary>
    /// Closes <paramref name="tab"/> (its ✕), once its form's unsaved changes are saved or thrown away: it's shown first, so
    /// the question is asked of the form in view. False when Cancel keeps it, or a Save refused does.
    /// </summary>
    public async Task<bool> CloseTabAsync(RecordTab<TForm> tab)
    {
        if (!Tabs.All.Contains(tab))
        {
            return true;
        }

        if (tab.Form.HasUnsavedChanges)
        {
            Tabs.Activate(tab);
            if (!await tab.Form.ResolveUnsavedChangesAsync(revert: false))
            {
                return false;
            }
        }

        Drop(tab);
        return true;
    }

    /// <summary>Takes <paramref name="tab"/> away with its form, asking nothing: its record is gone, or its changes were dealt with.</summary>
    protected void Drop(RecordTab<TForm> tab)
    {
        Tabs.Remove(tab);
        if (_wiring.Remove(tab, out var wiring))
        {
            wiring.Dispose();
        }

        tab.Form.Dispose();
    }

    /// <summary>
    /// Deletes the record <paramref name="recordId"/> (<paramref name="delete"/>) once <paramref name="question"/> is answered
    /// Yes, asked as <paramref name="asker"/>'s question (a form's Delete, for its own; the view's own with none, the list's,
    /// for any). The tab holding it closes, as it's gone, and the view loads again.
    /// </summary>
    protected Task DeleteRecordAsync(int recordId, TForm? asker, string question, Func<Task> delete) =>
        ConfirmedAsync(asker, question, async () =>
        {
            await delete();
            if (Tabs.Holding(recordId) is { } open)
            {
                Drop(open);
            }

            await LoadCommand.ExecuteAsync();
        });

    /// <summary>Runs <paramref name="action"/> once <paramref name="question"/> is answered Yes, as <paramref name="asker"/>'s (the view's own with none).</summary>
    protected Task ConfirmedAsync(TForm? asker, string question, Func<Task> action) =>
        asker is IFormPartHost form ? form.RunWithConfirmationAsync(question, action) : RunWithConfirmationAsync(question, action);

    /// <summary>What runs <paramref name="asker"/>'s work and shows what it says: the form, or else the form showing (the view's part host).</summary>
    protected IScreenPartHost PartOf(TForm? asker) => asker is IScreenPartHost form ? form : this;

    /// <summary>
    /// A form shows another record, or none (<paramref name="showsSavedRecord"/> when it's a saved one): the list follows it
    /// while it's the one showing, and one opened, by its Save included, ends the new record's row.
    /// </summary>
    internal void FormShowsAnother(TForm form, bool showsSavedRecord)
    {
        if (form != Form)
        {
            return;
        }

        if (showsSavedRecord)
        {
            ViewList?.EndNewRow();
        }

        ViewList?.SyncTreeSelection();
    }

    /// <summary>Runs <paramref name="merge"/> within each of <paramref name="forms"/>' own refresh, one inside another, so nothing the view's load writes is taken for an edit.</summary>
    protected static void RefreshingEach(IReadOnlyList<TForm> forms, Action merge) => RefreshingEach(forms, merge, 0);

    private static void RefreshingEach(IReadOnlyList<TForm> forms, Action merge, int from)
    {
        if (from == forms.Count)
        {
            merge();
            return;
        }

        forms[from].RefreshWith(() => RefreshingEach(forms, merge, from + 1));
    }

    IReadOnlyList<IUnsavedChangesScreen> IOpenForms.OpenForms => [.. Tabs.All.Select(tab => tab.Form)];

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        foreach (var (tab, wiring) in _wiring)
        {
            wiring.Dispose();
            tab.Form.Dispose();
        }

        _wiring.Clear();
    }

    // IRecordScreen, for the Vim command line: the form showing's Save and Delete, and while none shows, nothing to do.
    ScreenCommand IRecordScreen.SaveCommand => (Form as IRecordScreen)?.SaveCommand ?? _nothing;

    ScreenCommand IRecordScreen.DeleteCommand => (Form as IRecordScreen)?.DeleteCommand ?? _nothing;

    // What the list asks of the view (IRecordListHost): the form showing, and its part host. The list's own work runs as the
    // form showing does, as the view's load does.
    bool IRecordListHost.IsDirty => Form?.HasUnsavedChanges == true;

    string? IRecordListHost.ErrorMessage => Runner.ErrorMessage;

    // A new record takes a tab of its own, or the preview tab, and replaces no form with changes: nothing to ask.
    Task IRecordListHost.AfterUnsavedChangesAsync(Action then)
    {
        then();
        return Task.CompletedTask;
    }

    // Every tab's check for the records a write takes: a move of several can take records two tabs hold with changes.
    IReadOnlyList<SavedVersionCheck> IRecordListHost.SavedVersionsFor(IEnumerable<int>? ids)
    {
        var wanted = ids?.ToHashSet();
        return [.. Tabs.All.SelectMany(tab => (tab.Form as IRecordListHost)?.SavedVersionsFor(wanted) ?? [])];
    }

    // The list saved these records itself (a rename, a move): each form holding one takes its new saved time at the load.
    Task IRecordListHost.LoadAfterSavingAsync(IEnumerable<int> ids)
    {
        var saved = ids.ToList();
        foreach (var tab in Tabs.All)
        {
            tab.Form.FollowOwnSaveOf(saved);
        }

        return LoadCommand.ExecuteAsync();
    }

    bool IFormPartHost.IsBusyApplying => Form is IFormPartHost form ? form.IsBusyApplying : IsBusyApplying;

    Task IFormPartHost.RunWithConfirmationAsync(string question, Func<Task> action) => ConfirmedAsync(Form, question, action);

    void IFormPartHost.ApplyRefresh(Action merge) => ApplyRefresh(merge);

    BackgroundWork IScreenPartHost.Work => Form is IScreenPartHost form ? form.Work : Work;

    Task IScreenPartHost.RunSafelyAsync(Func<Task> action) => Form is IScreenPartHost form ? form.RunSafelyAsync(action) : RunSafelyAsync(action);

    void IScreenPartHost.LoadLatest<T>(IObservable<T> values, Func<T, CancellationToken, Task> load) => LoadLatest(values, load, () => Runner);

    void IScreenPartHost.ShowError(string? message)
    {
        if (Form is IScreenPartHost form)
        {
            form.ShowError(message);
        }
        else
        {
            ErrorMessage = message;
        }
    }

    void IScreenPartHost.ShowSuccess(string? message)
    {
        if (Form is IScreenPartHost form)
        {
            form.ShowSuccess(message);
        }
        else
        {
            SuccessMessage = message;
        }
    }

    void IScreenPartHost.ShowFailure(Exception exception, string? context)
    {
        if (Form is IScreenPartHost form)
        {
            form.ShowFailure(exception, context);
        }
        else
        {
            ShowFailure(exception, context);
        }
    }

    void IUndoRecorder.BeginUndoStep() => (Form as IUndoRecorder)?.BeginUndoStep();

    void IUndoRecorder.EndUndoStep() => (Form as IUndoRecorder)?.EndUndoStep();

    bool IUndoRecorder.IsEditPending
    {
        get => Form is IUndoRecorder form && form.IsEditPending;
        set
        {
            if (Form is IUndoRecorder form)
            {
                form.IsEditPending = value;
            }
        }
    }

    int IUndoRecorder.FormVersion => (Form as IUndoRecorder)?.FormVersion ?? 0;
}
