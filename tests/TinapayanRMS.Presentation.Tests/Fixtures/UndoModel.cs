using AwesomeAssertions;
using TinapayanRMS.Presentation.Shared;

namespace TinapayanRMS.Presentation.Tests.Fixtures;

/// <summary>
/// What a draft's undo history should hold, kept beside the real one in a random sequence (docs/adr/0006): every state a
/// kept step left, where the history stands among them, and what was last saved. A step that changes nothing the history
/// compares keeps nothing, and leaves what an undo stepped off to be redone; one that does starts a new branch.
/// </summary>
/// <param name="start">The draft as it stood when its history last started: loaded, saved or new.</param>
/// <param name="showsSame">What the history compares a step's two ends by.</param>
/// <param name="savesSame">What its dirty check compares the draft with what was saved by; <paramref name="showsSame"/> by default.</param>
/// <param name="standsAt">
/// What the draft is compared with the state its history stands at by; <paramref name="showsSame"/> by default. For a draft
/// whose snapshots hold its costs, the comparison that leaves them out: a load reprices the draft and none of its steps.
/// </param>
internal sealed class UndoModel<TSnapshot>(
    TSnapshot start,
    Func<TSnapshot, TSnapshot, bool> showsSame,
    Func<TSnapshot, TSnapshot, bool>? savesSame = null,
    Func<TSnapshot, TSnapshot, bool>? standsAt = null)
    where TSnapshot : class
{
    private readonly List<TSnapshot> _states = [start];
    private int _at;
    private TSnapshot _saved = start;

    /// <summary>A step taken, from <paramref name="before"/> to <paramref name="after"/>: kept when they differ.</summary>
    public void Edited(TSnapshot before, TSnapshot after)
    {
        if (!showsSame(after, before))
        {
            _states.RemoveRange(_at + 1, _states.Count - _at - 1);
            _states.Add(after);
            _at++;
        }
    }

    /// <summary>An undo tried: it moved exactly when there was a kept step to step back over.</summary>
    public void Undone(bool moved) => Moved(moved, -1);

    /// <summary>A redo tried: it moved exactly when an undo had stepped off something not since overwritten.</summary>
    public void Redone(bool moved) => Moved(moved, +1);

    /// <summary>
    /// Every state, and what was saved, through <paramref name="rewrite"/>, as the history's own are (UndoHistory.RewriteAll):
    /// for a change saved elsewhere that no undo may take back.
    /// </summary>
    public void Rewritten(Func<TSnapshot, TSnapshot> rewrite)
    {
        for (var i = 0; i < _states.Count; i++)
        {
            _states[i] = rewrite(_states[i]);
        }

        _saved = rewrite(_saved);
    }

    /// <summary>A Save written: <paramref name="now"/> is what was saved, with nothing to undo or redo.</summary>
    public void Saved(TSnapshot now)
    {
        _saved = now;
        _states.Clear();
        _states.Add(now);
        _at = 0;
    }

    /// <summary>
    /// The history and the draft, <paramref name="now"/>, as the model says: the draft where the history stands, dirty exactly
    /// when it differs from what was saved, and Undo and Redo offered exactly when there's somewhere to step.
    /// </summary>
    public void ShouldMatch(UndoHistory<TSnapshot> history, TSnapshot now, string when)
    {
        (standsAt ?? showsSame)(now, _states[_at]).Should().BeTrue($"{when}, the draft is where its history stands");
        history.IsDirty.Should().Be(!(savesSame ?? showsSame)(now, _saved), $"{when}, the dirty check says whether it differs from what was last saved");
        history.CanUndo.Should().Be(_at > 0, $"{when}, Undo is offered while there's a step to take back");
        history.CanRedo.Should().Be(_at < _states.Count - 1, $"{when}, Redo is offered while there's an undone step to take again");
    }

    private void Moved(bool moved, int by)
    {
        var to = _at + by;
        moved.Should().Be(to >= 0 && to < _states.Count, "the history steps as far as its steps go, and no further");
        if (moved)
        {
            _at = to;
        }
    }
}
