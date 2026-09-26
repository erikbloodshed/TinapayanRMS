using ReactiveUI;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// What a screen offers the Controls/ behaviours that record undo steps, so a behaviour never needs to know
/// which screen it's on: it brackets the gesture it can see and the screen decides whether anything changed.
/// Opening a step settles any step still open, so a gesture that's abandoned rather than finished — Esc on
/// the add row, a cell edit a rule refused — costs nothing and needs no cancel path of its own.
/// </summary>
public interface IUndoRecorder
{
    /// <summary>A change is about to be made: remember how the form stands now.</summary>
    void BeginUndoStep();

    /// <summary>The change is finished: keep the step if it changed anything, drop it if it didn't.</summary>
    void EndUndoStep();

    /// <summary>
    /// Something is being typed that hasn't reached the form yet: a grid cell still open, or a box whose value
    /// only lands when focus leaves it. Until it does, the screen can't tell whether there's anything to save,
    /// so it keeps Save offered while this is set — the click that saves is also the click that ends the edit,
    /// and a Save button disabled under the cursor would swallow it.
    /// </summary>
    bool IsEditPending { get; set; }
}

/// <summary>
/// A screen that can step its form back and forward as well as record the steps. It's what Ctrl+Z looks for: the
/// shortcut finds the screen the cursor is on through this, so it needs to know nothing about which screen that is,
/// and a window showing one that doesn't offer it — Settings, a prompt — keeps the key for itself.
/// </summary>
public interface IUndoScreen : IUndoRecorder
{
    /// <summary>Puts the form back one step. False when there was nothing to undo.</summary>
    bool TryUndo();

    /// <summary>Steps forward into what the last undo stepped off. False when there was nothing to redo.</summary>
    bool TryRedo();
}

/// <summary>
/// One screen's undo and redo stacks. It knows nothing of what a snapshot holds: the screen hands it how to
/// take one, how to tell two apart, and when it's allowed to record at all. Products uses it first; another
/// screen takes it with its own snapshot type and nothing else to write.
/// </summary>
/// <param name="capture">Takes the screen's editable state as it stands.</param>
/// <param name="showsSame">True when two snapshots would look the same to the user, so no step is worth keeping.</param>
/// <param name="canRecord">False while the screen is mid-load or mid-restore, when a change isn't the user's.</param>
/// <param name="savesSame">
/// True when two snapshots would be saved the same, which is the narrower question behind <see cref="IsDirty"/>:
/// a screen that holds nothing but the record's own data answers it with <paramref name="showsSame"/>, which is
/// the default, and one that also holds where the user has got to gives its own.
/// </param>
/// <param name="depth">How many steps are kept; the oldest goes when a new one passes it.</param>
public sealed class UndoHistory<TSnapshot>(
    Func<TSnapshot> capture,
    Func<TSnapshot, TSnapshot, bool> showsSame,
    Func<bool> canRecord,
    Func<TSnapshot, TSnapshot, bool>? savesSame = null,
    int depth = 50) : ReactiveObject
    where TSnapshot : class
{
    // Lists rather than Stacks: capping the depth means dropping from the far end, which a Stack can't do. Each step
    // keeps the settled form as it stood when the step was taken beside it (see TryUndo's second overload).
    private readonly List<(TSnapshot Snapshot, TSnapshot? Settled)> _undo = [];
    private readonly List<(TSnapshot Snapshot, TSnapshot? Settled)> _redo = [];

    /// <summary>How the form stood when the step now open was opened; null when no step is open.</summary>
    private TSnapshot? _pending;

    /// <summary>The settled form as it stood when the step now open was opened.</summary>
    private TSnapshot? _pendingSettled;

    /// <summary>
    /// How the form stood when it was last settled: loaded, saved or reverted. Null between a <see cref="Clear"/>
    /// and the <see cref="MarkClean"/> that follows it, while the form is mid-load and holds nothing of the user's.
    /// </summary>
    private TSnapshot? _baseline;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    private bool _isDirty;

    /// <summary>
    /// Whether the form holds anything a Save would write. It's what the Save button is enabled by, so it has to
    /// follow the form as it's typed into rather than only as each field is left: a button still disabled under
    /// the cursor swallows the very click that would have enabled it. The screen calls <see cref="RefreshDirty"/>
    /// for every change it can see; this class calls it for the ones it settles itself.
    /// </summary>
    public bool IsDirty
    {
        get => _isDirty;
        private set => this.RaiseAndSetIfChanged(ref _isDirty, value);
    }

    private bool _isEditPending;

    /// <summary>
    /// An edit that hasn't reached the form yet, as <see cref="IUndoRecorder.IsEditPending"/> describes. While
    /// it's set, <see cref="IsDirty"/> says there is something to save, since nothing else can say otherwise.
    /// </summary>
    public bool IsEditPending
    {
        get => _isEditPending;
        set
        {
            _isEditPending = value;
            RefreshDirty();
        }
    }

    /// <summary>
    /// Takes the form as it stands for the settled one: what a Save has just written, or what a load has just put
    /// there. Called once the screen's details have all arrived, since a form measured half-loaded would read as
    /// changed the moment the rest of it landed.
    /// </summary>
    public void MarkClean()
    {
        _baseline = capture();
        RefreshDirty();
    }

    /// <summary>Re-reads whether the form still shows what it was settled with. Cheap enough for every keystroke.</summary>
    public void RefreshDirty() =>
        IsDirty = _isEditPending || (_baseline is not null && !(savesSame ?? showsSame)(_baseline, capture()));

    private void RefreshDirty(TSnapshot now) =>
        IsDirty = _isEditPending || (_baseline is { } baseline && !(savesSame ?? showsSame)(baseline, now));

    /// <summary>
    /// Moves part of the settled form on to what the form holds now, for a change that isn't the user's: a
    /// section's form opened over the previous one shows other values without anything having been edited. The
    /// screen says which part through <paramref name="rebase"/>, which is handed the settled form and the one
    /// showing and returns the settled form to measure against from here.
    /// </summary>
    public void Rebase(Func<TSnapshot, TSnapshot, TSnapshot> rebase)
    {
        if (_baseline is { } baseline)
        {
            _baseline = rebase(baseline, capture());
            RefreshDirty();
        }
    }

    /// <summary>
    /// Changes every snapshot the history holds through <paramref name="rewrite"/>: the settled form, the step open,
    /// and each undo and redo step with the settled form beside it. It's for a change saved from outside the form,
    /// which no undo or redo may take back: a product dragged into another category in the Products list. Rebasing
    /// only the settled form would leave the steps before it holding the old value, so undoing one would put it
    /// back, and a Save then undo the change unseen.
    /// </summary>
    public void RewriteAll(Func<TSnapshot, TSnapshot> rewrite)
    {
        _baseline = _baseline is null ? null : rewrite(_baseline);
        _pending = _pending is null ? null : rewrite(_pending);
        _pendingSettled = _pendingSettled is null ? null : rewrite(_pendingSettled);
        RewriteSteps(_undo);
        RewriteSteps(_redo);
        RefreshDirty();

        void RewriteSteps(List<(TSnapshot Snapshot, TSnapshot? Settled)> steps)
        {
            for (var i = 0; i < steps.Count; i++)
            {
                var (snapshot, settled) = steps[i];
                steps[i] = (rewrite(snapshot), settled is null ? null : rewrite(settled));
            }
        }
    }

    /// <summary>Opens a step, settling one already open first.</summary>
    public void Begin()
    {
        End();
        if (canRecord())
        {
            _pending = capture();
            _pendingSettled = _baseline;
        }
    }

    /// <summary>Whether a step is open: begun and not yet ended.</summary>
    public bool IsStepOpen => _pending is not null;

    /// <summary>Closes the open step, keeping it only if the form actually changed while it was open.</summary>
    public void End() => End(rebase: null);

    /// <summary>
    /// Closes the open step as <see cref="End()"/> does, but measures it from where it began moved on by
    /// <paramref name="rebase"/>, as <see cref="Rebase"/> moves the settled form: for a change that carries along
    /// a part that isn't the user's. What's kept is still where it began, so an undo puts all of it back.
    /// </summary>
    public void End(Func<TSnapshot, TSnapshot, TSnapshot>? rebase)
    {
        if (_pending is not { } before)
        {
            return;
        }

        var settled = _pendingSettled;
        _pending = null;
        _pendingSettled = null;
        if (!canRecord())
        {
            return;
        }

        var now = capture();
        RefreshDirty(now);
        if (showsSame(rebase is null ? before : rebase(before, now), now))
        {
            return;
        }

        _undo.Add((before, settled));
        if (_undo.Count > depth)
        {
            _undo.RemoveAt(0);
        }

        // A change made after an undo is a new branch, so there's nothing left to step forward into.
        var hadRedo = _redo.Count > 0;
        _redo.Clear();
        RaiseCanChanged(redoToo: hadRedo);
    }

    /// <summary>Hands back the step before the last change, putting the form as it stands now on the redo stack.</summary>
    public bool TryUndo(out TSnapshot restore) => TryMove(_undo, _redo, out restore, out _);

    /// <summary>
    /// <see cref="TryUndo(out TSnapshot)"/>, also handing back the settled form as it stood when that step was taken
    /// (null if none had been settled yet). A screen that rebases the settled form as the user moves around
    /// (<see cref="Rebase"/>) takes the rebased part back from it: the restore puts back where the user was, and what
    /// was measured against there has to come back with it. Without it, the settled form still points wherever the
    /// step being undone moved it, and a form put back just as it was saved reads as changed.
    /// </summary>
    public bool TryUndo(out TSnapshot restore, out TSnapshot? settled) => TryMove(_undo, _redo, out restore, out settled);

    /// <summary>Hands back the step an undo stepped off, putting the form as it stands now back on the undo stack.</summary>
    public bool TryRedo(out TSnapshot restore) => TryMove(_redo, _undo, out restore, out _);

    /// <summary><see cref="TryRedo(out TSnapshot)"/>, with the settled form as <see cref="TryUndo(out TSnapshot, out TSnapshot?)"/> hands it back.</summary>
    public bool TryRedo(out TSnapshot restore, out TSnapshot? settled) => TryMove(_redo, _undo, out restore, out settled);

    private bool TryMove(
        List<(TSnapshot Snapshot, TSnapshot? Settled)> from, List<(TSnapshot Snapshot, TSnapshot? Settled)> to, out TSnapshot restore, out TSnapshot? settled)
    {
        restore = default!;
        settled = null;
        if (from.Count == 0)
        {
            return false;
        }

        var last = from.Count - 1;
        (restore, settled) = from[last];
        from.RemoveAt(last);
        to.Add((capture(), _baseline));
        RaiseCanChanged(redoToo: true);
        return true;
    }

    /// <summary>
    /// Throws the history away: another product picked, a new one started, a Save or a Revert. The settled form
    /// goes with it, so nothing is dirty until the screen says what the new one is (<see cref="MarkClean"/>) —
    /// a form still being loaded holds no change of the user's to save.
    /// </summary>
    public void Clear()
    {
        _pending = null;
        _pendingSettled = null;
        _baseline = null;
        RefreshDirty();
        var hadUndo = _undo.Count > 0;
        var hadRedo = _redo.Count > 0;
        _undo.Clear();
        _redo.Clear();
        if (hadUndo || hadRedo)
        {
            RaiseCanChanged(redoToo: true);
        }
    }

    private void RaiseCanChanged(bool redoToo)
    {
        this.RaisePropertyChanged(nameof(CanUndo));
        if (redoToo)
        {
            this.RaisePropertyChanged(nameof(CanRedo));
        }
    }
}
