namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// A figure worked out from a screen's form (a batch's totals, a product's costs): computed when it's read, and only when
/// something it's worked out from has changed since (<see cref="Invalidate"/>), so a screen drawn again for anything else
/// reads it for nothing. Its owner exposes what it shows as plain getters over <see cref="Value"/>.
/// </summary>
/// <remarks>
/// Marking it stale too often costs one more computation, at the next read; too rarely shows a stale figure. So the owner
/// marks it stale broadly, from every change of what it reads (its rows' changes included), not one setter at a time. An
/// undo that writes thirty fields marks it stale thirty times and it's worked out once, from the whole form, when it's
/// next read. Only read it on the UI thread, as everything else a screen's state holds.
/// </remarks>
/// <typeparam name="T">The figure: a record of what's worked out together, or a single value.</typeparam>
public sealed class Derived<T>
{
    private readonly Func<T> _compute;
    private readonly Action _changed;
    private T _value = default!;
    private bool _isStale = true;

    /// <param name="compute">Works the figure out from what it reads, as it stands.</param>
    /// <param name="changed">
    /// Says the figure may have changed, so whatever shows it reads it again: the owner raises a change of the properties
    /// that show it (ScreenBase draws the screen again on any change of a part). Called once a stale figure has been read,
    /// not on every change, so a pass through every row asks for one redraw.
    /// </param>
    public Derived(Func<T> compute, Action changed)
    {
        _compute = compute;
        _changed = changed;
    }

    /// <summary>The figure, worked out now if anything it's worked out from changed since it was last read.</summary>
    public T Value
    {
        get
        {
            if (_isStale)
            {
                _value = _compute();
                _isStale = false;
            }

            return _value;
        }
    }

    /// <summary>Something the figure is worked out from changed: it's worked out again when next read.</summary>
    public void Invalidate()
    {
        if (_isStale)
        {
            return;
        }

        _isStale = true;
        _changed();
    }
}
