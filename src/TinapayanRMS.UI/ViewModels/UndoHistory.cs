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
/// <param name="depth">How many steps are kept; the oldest goes when a new one passes it.</param>
public sealed class UndoHistory<TSnapshot>(
    Func<TSnapshot> capture,
    Func<TSnapshot, TSnapshot, bool> showsSame,
    Func<bool> canRecord,
    int depth = 50) : ReactiveObject
    where TSnapshot : class
{
    // Lists rather than Stacks: capping the depth means dropping from the far end, which a Stack can't do.
    private readonly List<TSnapshot> _undo = [];
    private readonly List<TSnapshot> _redo = [];

    /// <summary>How the form stood when the step now open was opened; null when no step is open.</summary>
    private TSnapshot? _pending;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>Opens a step, settling one already open first.</summary>
    public void Begin()
    {
        End();
        if (canRecord())
        {
            _pending = capture();
        }
    }

    /// <summary>Closes the open step, keeping it only if the form actually changed while it was open.</summary>
    public void End()
    {
        if (_pending is not { } before)
        {
            return;
        }

        _pending = null;
        if (!canRecord() || showsSame(before, capture()))
        {
            return;
        }

        _undo.Add(before);
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
    public bool TryUndo(out TSnapshot restore) => TryMove(_undo, _redo, out restore);

    /// <summary>Hands back the step an undo stepped off, putting the form as it stands now back on the undo stack.</summary>
    public bool TryRedo(out TSnapshot restore) => TryMove(_redo, _undo, out restore);

    private bool TryMove(List<TSnapshot> from, List<TSnapshot> to, out TSnapshot restore)
    {
        restore = default!;
        if (from.Count == 0)
        {
            return false;
        }

        var last = from.Count - 1;
        restore = from[last];
        from.RemoveAt(last);
        to.Add(capture());
        RaiseCanChanged(redoToo: true);
        return true;
    }

    /// <summary>Throws the history away: another product picked, a new one started, a Save or a Revert.</summary>
    public void Clear()
    {
        _pending = null;
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
