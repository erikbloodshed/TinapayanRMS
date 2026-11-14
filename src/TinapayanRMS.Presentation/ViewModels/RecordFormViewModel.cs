using System.ComponentModel;
using System.Reactive.Linq;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using ILogger = Serilog.ILogger;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>A record screen's form as it stands at one instant: what an undo step puts back, and what Save compares.</summary>
public interface IFormSnapshot<in TSnapshot>
{
    /// <summary>True when two snapshots would look the same to the user, so no undo step is worth keeping.</summary>
    bool ShowsSameAs(TSnapshot other);

    /// <summary>
    /// True when two snapshots would be saved the same, which is what Save is enabled by: <see cref="ShowsSameAs"/> unless
    /// the form holds something no Save writes (Products' Clone name, Raw Materials' Per unit).
    /// </summary>
    bool SavesSameAs(TSnapshot other) => ShowsSameAs(other);
}

/// <summary>
/// What the three record screens (Products, Raw Materials, Sub-Assemblies) share about their form: its undo and redo
/// (<see cref="History"/>, the Undo and Redo commands, the steps the fields record through <see cref="IUndoRecorder"/>),
/// whether it holds anything to save, the question about unsaved changes before it's left, and opening a record by its id
/// or name (<see cref="OpenRecord"/>). Each screen says how its form is captured and put back, what it's called in the
/// question, how it saves and how it goes back to what was saved, and how a record is found and picked in its list.
/// </summary>
public abstract class RecordFormViewModel<TSnapshot> : ViewModelBase, IUndoScreen, IUnsavedChangesScreen, IRecordListHost, IRecordOpener
    where TSnapshot : class, IFormSnapshot<TSnapshot>
{
    /// <summary>
    /// A record for LoadCommand to open at its end: one <see cref="OpenRecord"/> was asked for before the list held it, or
    /// the one in the screen's address (<see cref="OpenRecordOnLoad"/>). New drops it (<see cref="ForgetRecordToOpen"/>).
    /// </summary>
    private int? _recordToOpen;

    protected RecordFormViewModel(ILogger logger)
        : base(logger)
    {
        // Made first, so it's there for every subscription a screen wires up, which run as they're wired. Nothing is
        // recorded while CanRecordUndo says the form isn't the user's yet.
        History = new UndoHistory<TSnapshot>(
            CaptureSnapshot,
            (before, after) => before.ShowsSameAs(after),
            () => CanRecordUndo,
            (settled, now) => settled.SavesSameAs(now));
        UndoCommand = ReactiveCommand.Create(() => { TryUndo(); }, HistoryFlag(nameof(UndoHistory<TSnapshot>.CanUndo), h => h.CanUndo));
        RedoCommand = ReactiveCommand.Create(() => { TryRedo(); }, HistoryFlag(nameof(UndoHistory<TSnapshot>.CanRedo), h => h.CanRedo));
    }

    /// <summary>
    /// One of History's flags as it is and as it changes. Read from its Changed stream rather than WhenAnyValue, whose
    /// generated code can't stand in for a property of a type built from a type parameter.
    /// </summary>
    private IObservable<bool> HistoryFlag(string name, Func<UndoHistory<TSnapshot>, bool> read) =>
        History.Changed
            .Where(change => change.PropertyName == name)
            .Select(_ => read(History))
            .StartWith(read(History))
            .DistinctUntilChanged();

    /// <summary>
    /// The form's undo and redo stacks. One step is one finished change (a cell edit committed, a row added or taken out,
    /// a field typed and left), never a keystroke. It holds what the screen shows, not what Save writes: undoing never
    /// touches what Save compares against, so a change undone and one never made come to the same thing. Cleared whenever
    /// the form stops being the one it recorded: another record picked, a new one started, a Save, or a Revert. Get-only,
    /// so the screen watches it as one of the ViewModel's parts (ScreenBase).
    /// </summary>
    public UndoHistory<TSnapshot> History { get; }

    public ReactiveCommand<RxVoid, RxVoid> UndoCommand { get; }

    public ReactiveCommand<RxVoid, RxVoid> RedoCommand { get; }

    /// <summary>IUndoRecorder: goes up as another record, or a new one, takes the form's place.</summary>
    public int FormVersion { get; protected set; }

    /// <summary>The form as it stands: everything an undo step puts back.</summary>
    protected abstract TSnapshot CaptureSnapshot();

    /// <summary>
    /// Puts the whole form back as <paramref name="snapshot"/> had it. <paramref name="settledThen"/> is what Save measured
    /// against when it was taken (null if nothing had been settled yet), for a screen that moves what it measures against
    /// as the user moves around (Products' sub-assembly form).
    /// </summary>
    protected abstract void RestoreSnapshot(TSnapshot snapshot, TSnapshot? settledThen);

    /// <summary>
    /// Whether a change now is the user's, to record as an undo step: not while a load merges fresh data or an undo puts
    /// a form back (IsBusyApplying), and not, on a screen that reads a record's details off the UI thread, until they've
    /// arrived.
    /// </summary>
    protected virtual bool CanRecordUndo => !IsBusyApplying;

    /// <summary>What the question about unsaved changes calls the form: the name typed, or the record's, or "the new …".</summary>
    protected abstract string UnsavedChangesName { get; }

    /// <summary>Saves the form, as its Save does; the question's Yes. Whether it went through is read from History.IsDirty.</summary>
    protected abstract Task SaveFormAsync();

    /// <summary>Puts the form back as the record was last saved, or a blank one for a new record; Don't Save on leaving the screen.</summary>
    protected abstract Task RevertToSavedAsync();

    void IUndoRecorder.BeginUndoStep() => History.Begin();

    void IUndoRecorder.EndUndoStep() => History.End();

    bool IUndoRecorder.IsEditPending
    {
        get => History.IsEditPending;
        set => History.IsEditPending = value;
    }

    /// <summary>Records <paramref name="change"/> as one undo step, for a command that makes its change in one go.</summary>
    protected void AsUndoStep(Action change) => ((IUndoRecorder)this).AsUndoStep(change);

    /// <summary>Settles whatever step is open, then puts the form back one step. False when there was nothing to undo.</summary>
    public bool TryUndo()
    {
        History.End();
        if (!History.TryUndo(out var restore, out var settled))
        {
            return false;
        }

        RestoreSnapshot(restore, settled);
        return true;
    }

    /// <summary>Steps forward again into what the last undo stepped off. False when there was nothing to redo.</summary>
    public bool TryRedo()
    {
        History.End();
        if (!History.TryRedo(out var restore, out var settled))
        {
            return false;
        }

        RestoreSnapshot(restore, settled);
        return true;
    }

    /// <summary>
    /// Follows <paramref name="rows"/> for Save's sake: a row added, removed or replaced can be something to save. Watched
    /// through the collection's own PropertyChanged rather than its CollectionChanged, because an ObservableCollection only
    /// enforces its re-entrancy guard once more than one handler is listening, and these collections are changed from
    /// inside their own notifications, so a second CollectionChanged listener would turn a change the guard tolerates today
    /// into an exception. What's typed into a row reaches Save another way: the grid closes the cell's undo step, which
    /// re-reads whether there's anything to save.
    /// </summary>
    protected void WatchForSave(INotifyPropertyChanged rows) => rows.PropertyChanged += (_, _) => History.RefreshDirty();

    public bool HasUnsavedChanges => History.IsDirty;

    /// <summary>The form's last Save (<see cref="RunSaveAsync"/>), done or still running.</summary>
    private Task _saving = Task.CompletedTask;

    /// <summary>
    /// Runs the form's Save, the body of the screen's SaveCommand, as <see cref="ViewModelBase.RunSafelyAsync(Func{Task})"/>
    /// does, and keeps it while it runs. The form holds unsaved changes until its Save is done, so the question about them
    /// waits for it (<see cref="ResolveUnsavedChangesAsync"/>): asked meanwhile (Save, then a tab or the window's Close
    /// before it was back), its Yes started a second Save of the same form beside the first, since SaveCommand.Execute()
    /// runs whatever CanExecute says. Kept here rather than read from the command's IsExecuting, which ReactiveUI sets on
    /// its output scheduler, and so not always by the time Execute() returns.
    /// </summary>
    protected Task RunSaveAsync(Func<Task> save) => _saving = RunSafelyAsync(save);

    /// <inheritdoc/>
    /// <remarks>
    /// Waits for a Save still running first, only while the form is dirty: a Save clears the form's history before it
    /// reloads, so nothing a Save runs on its way can wait for that Save itself.
    /// </remarks>
    public async Task<bool> ResolveUnsavedChangesAsync(bool revert)
    {
        if (History.IsDirty)
        {
            await _saving;
        }

        return await AskAboutUnsavedChangesAsync(
            History.IsDirty,
            UnsavedChangesName,
            async () =>
            {
                await SaveFormAsync();
                return !History.IsDirty;
            },
            revert ? RevertToSavedAsync : null);
    }

    /// <summary>
    /// A record picked in the list, by <paramref name="id"/>: shown at once (<paramref name="pick"/>), or, while the form holds
    /// unsaved changes, once they're saved or thrown away. The list calls this in the middle of changing its selection, so
    /// the question waits for that to finish, puts the list back on the record showing (<paramref name="showListOnShowing"/>),
    /// then opens the one picked (<see cref="OpenRecordAsync"/>, which asks). Nothing awaits it: what fails shows on the
    /// form (RunSafelyAsync).
    /// </summary>
    protected void PickFromList(int id, Action pick, Action showListOnShowing)
    {
        if (!History.IsDirty)
        {
            pick();
            return;
        }

        _ = RunSafelyAsync(async () =>
        {
            await Task.Yield();
            showListOnShowing();
            await OpenRecordAsync(id);
        });
    }

    /// <summary>Runs <paramref name="then"/>, which replaces the record showing, once its unsaved changes are saved or thrown away.</summary>
    protected async Task AfterUnsavedChangesAsync(Action then)
    {
        if (await ResolveUnsavedChangesAsync(revert: false))
        {
            then();
        }
    }

    /// <summary>
    /// Picks the record <paramref name="id"/> as picking its row in the list would, clearing a search or filter that hides
    /// the row. False when the list doesn't hold it (yet).
    /// </summary>
    protected abstract bool TrySelectRecord(int id);

    /// <summary>The id of the record <paramref name="text"/> names best among the list's (RecordNameMatch); null for none.</summary>
    protected abstract int? FindRecordByName(string text);

    /// <summary>
    /// Whether opening the record <paramref name="id"/> replaces the one the form shows, so its unsaved changes are asked
    /// about first: any record but the one showing.
    /// </summary>
    protected abstract bool ReplacesShowing(int id);

    /// <summary>
    /// Opens a record as picking it in the list would, for another screen (an address of the record's: Back, Forward, a
    /// link). A search hiding it is cleared, so the list shows the row picked. A list that doesn't hold it yet (the screen not
    /// loaded since, or ever) opens it at the end of the next LoadCommand, which showing the screen runs.
    /// </summary>
    public void OpenRecord(int id) => _recordToOpen = TrySelectRecord(id) ? null : id;

    /// <summary>
    /// Opens a record at the end of the next LoadCommand, for the one in the screen's address as the screen opens: its list
    /// is fresh only then. New clicked meanwhile is the later word, and the record isn't opened over it.
    /// </summary>
    public void OpenRecordOnLoad(int id) => _recordToOpen = id;

    /// <summary>
    /// <see cref="OpenRecord"/>, asking first about the unsaved changes of the record showing when another is to replace it
    /// (<see cref="ReplacesShowing"/>). False when the user stays on it.
    /// </summary>
    public async Task<bool> OpenRecordAsync(int id)
    {
        if (ReplacesShowing(id) && !await ResolveUnsavedChangesAsync(revert: false))
        {
            return false;
        }

        OpenRecord(id);
        return true;
    }

    /// <summary>IRecordScreen, for the Vim command line's <c>:e name</c>: opens the record the name means best.</summary>
    public bool OpenByName(string text)
    {
        if (FindRecordByName(text) is not { } id)
        {
            return false;
        }

        _ = RunSafelyAsync(() => OpenRecordAsync(id));
        return true;
    }

    /// <summary>
    /// The end of a LoadCommand, outside its ApplyRefresh, whose selection changes only re-point: a record asked for before
    /// the list held it, or the one in the address, opens now, its form loading.
    /// </summary>
    protected void OpenRecordAskedFor()
    {
        if (_recordToOpen is { } id)
        {
            _recordToOpen = null;
            TrySelectRecord(id);
        }
    }

    /// <summary>
    /// New: a record still waiting for LoadCommand to open it no longer is. It would load over the new form, and a name typed
    /// into it since would then be saved as that record's.
    /// </summary>
    protected void ForgetRecordToOpen() => _recordToOpen = null;

    // What any part of the screen's form, and its list, asks of it (IFormPartHost, IRecordListHost).
    bool IFormPartHost.IsRefreshing => IsRefreshing;

    bool IFormPartHost.IsBusyApplying => IsBusyApplying;

    Task IFormPartHost.RunWithConfirmationAsync(string question, Func<Task> action) => RunWithConfirmationAsync(question, action);

    void IFormPartHost.ApplyRefresh(Action merge) => ApplyRefresh(merge);

    bool IRecordListHost.IsDirty => History.IsDirty;

    string? IRecordListHost.ErrorMessage => ErrorMessage;

    Task IRecordListHost.AfterUnsavedChangesAsync(Action then) => AfterUnsavedChangesAsync(then);
}
