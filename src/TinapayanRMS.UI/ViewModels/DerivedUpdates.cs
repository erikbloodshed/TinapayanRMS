namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A screen's derived updates: the figures it works out again from its form as the form changes (costs, a batch's weight,
/// what a form adds). Each runs at once as its inputs change, or, while a batch of writes is open (<see cref="Batch"/>, an
/// undo putting the whole form back), once when the batch ends, in the order first asked for. So a handler never has to
/// know whether the form is being restored: it asks, and runs from the finished form rather than a half-restored one.
/// </summary>
public sealed class DerivedUpdates
{
    private readonly List<Action> _pending = [];
    private int _openBatches;

    /// <summary>Whether a batch is open, its updates waiting for it to end.</summary>
    public bool IsBatching => _openBatches > 0;

    /// <summary>
    /// Runs <paramref name="update"/> now, or once when the open batch ends. The same update asked for again in a batch
    /// runs once: give it as the same method each time, so the two compare equal.
    /// </summary>
    public void Run(Action update)
    {
        if (_openBatches == 0)
        {
            update();
        }
        else if (!_pending.Contains(update))
        {
            _pending.Add(update);
        }
    }

    /// <summary>
    /// Runs <paramref name="writes"/> with every update they cause held back, then each of those once. Batches nest: the
    /// updates run as the outermost ends. Writes that throw drop what they held back, since the form they'd have worked
    /// from didn't come about.
    /// </summary>
    public void Batch(Action writes)
    {
        _openBatches++;
        var finished = false;
        try
        {
            writes();
            finished = true;
        }
        finally
        {
            _openBatches--;
            if (_openBatches == 0)
            {
                List<Action> held = [.. _pending];
                _pending.Clear();
                if (finished)
                {
                    foreach (var update in held)
                    {
                        update();
                    }
                }
            }
        }
    }
}
