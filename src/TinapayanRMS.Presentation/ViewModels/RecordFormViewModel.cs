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
/// whether it holds anything to save, and the question about unsaved changes before it's left. Each screen says how its
/// form is captured and put back, what it's called in the question, how it saves and how it goes back to what was saved.
/// </summary>
public abstract class RecordFormViewModel<TSnapshot> : ViewModelBase, IUndoScreen, IUnsavedChangesScreen
    where TSnapshot : class, IFormSnapshot<TSnapshot>
{
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
    protected void AsUndoStep(Action change)
    {
        History.Begin();
        try
        {
            change();
        }
        finally
        {
            History.End();
        }
    }

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

    /// <inheritdoc/>
    public Task<bool> ResolveUnsavedChangesAsync(bool revert) => AskAboutUnsavedChangesAsync(
        History.IsDirty,
        UnsavedChangesName,
        async () =>
        {
            await SaveFormAsync();
            return !History.IsDirty;
        },
        revert ? RevertToSavedAsync : null);

    /// <summary>Runs <paramref name="then"/>, which replaces the record showing, once its unsaved changes are saved or thrown away.</summary>
    protected async Task AfterUnsavedChangesAsync(Action then)
    {
        if (await ResolveUnsavedChangesAsync(revert: false))
        {
            then();
        }
    }
}
