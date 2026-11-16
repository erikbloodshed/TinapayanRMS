namespace TinapayanRMS.Presentation.Shared;

/// <summary>
/// The Category picker of a record's form that takes a name typed into it, on Raw Materials and Products: once the picker
/// is left, or Enter is pressed in it, the category of that name, whatever its case, is picked, and a name that isn't one
/// yet brings up a prompt to add it, so filing a record under a new category doesn't mean a trip to Settings. A cancelled
/// prompt, or a name refused, puts back the category the picker had; a name that turns out to be listed is picked instead.
/// The screen keeps what the page binds (its EditingCategory, the text typed and the prompt) and says how a category is
/// added; every change of the pick is one undo step.
/// </summary>
public sealed class CategoryPicker<TCategory>
    where TCategory : class
{
    private readonly Func<TCategory?> _current;
    private readonly Action<TCategory?> _pick;
    private readonly Func<IEnumerable<TCategory>> _options;
    private readonly Func<TCategory, string> _nameOf;
    private readonly Func<string> _typed;
    private readonly Action<string> _showText;
    private readonly Func<string, Task<string?>> _promptAsync;
    private readonly Func<string, Task<TCategory?>> _addAsync;
    private readonly Func<Func<Task>, Task> _runSafely;
    private readonly Action<string> _added;
    private readonly Action<Exception> _showFailure;

    // The category the picker held when it took the cursor, for a cancelled prompt to put back (Begin).
    private TCategory? _before;

    // Set while CommitAsync runs: its prompt takes the cursor from the picker, which would settle it again.
    private bool _isCommitting;

    /// <param name="current">The form's category now.</param>
    /// <param name="pick">Sets the form's category, as an undo step (AsUndoStep).</param>
    /// <param name="options">The categories the record can go in, which a name typed is looked for among.</param>
    /// <param name="nameOf">A category's name.</param>
    /// <param name="typed">What's typed into the picker (its CategorySearchText).</param>
    /// <param name="showText">Puts the category held back into the picker's text, in place of what was typed.</param>
    /// <param name="promptAsync">Asks whether to add a name typed, starting with it: the name to add, or null if cancelled.</param>
    /// <param name="addAsync">Saves a category of that name and reads it back; null, with the reason showing, if refused.</param>
    /// <param name="runSafely">The screen's RunSafelyAsync, which the save runs in.</param>
    /// <param name="added">Tells the user a category was added ("Dry Goods was added.").</param>
    /// <param name="showFailure">Shows what went wrong, from the picker, which nothing awaits.</param>
    public CategoryPicker(
        Func<TCategory?> current,
        Action<TCategory?> pick,
        Func<IEnumerable<TCategory>> options,
        Func<TCategory, string> nameOf,
        Func<string> typed,
        Action<string> showText,
        Func<string, Task<string?>> promptAsync,
        Func<string, Task<TCategory?>> addAsync,
        Func<Func<Task>, Task> runSafely,
        Action<string> added,
        Action<Exception> showFailure)
    {
        _current = current;
        _pick = pick;
        _options = options;
        _nameOf = nameOf;
        _typed = typed;
        _showText = showText;
        _promptAsync = promptAsync;
        _addAsync = addAsync;
        _runSafely = runSafely;
        _added = added;
        _showFailure = showFailure;
    }

    /// <summary>
    /// The picker took the cursor. Typing a name it doesn't list can clear its pick, so the category it holds now is
    /// remembered here, for a cancelled prompt to put back.
    /// </summary>
    public void Begin()
    {
        if (!_isCommitting)
        {
            _before = _current();
        }
    }

    /// <summary>
    /// Settles what's typed: the category of that name, whatever its case, is picked; a name that isn't one yet asks
    /// whether to add it. Nothing typed, or the name of the category held, leaves it as it is.
    /// </summary>
    public async Task CommitAsync()
    {
        var typed = _typed().Trim();
        if (_isCommitting || typed.Length == 0 || IsNamed(_current(), typed))
        {
            return;
        }

        _isCommitting = true;
        try
        {
            if (Listed(typed) is { } listed)
            {
                _pick(listed);
                return;
            }

            var name = (await _promptAsync(typed))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                _pick(_before);
            }
            else if (Listed(name) is { } named)
            {
                _pick(named);
            }
            else
            {
                await AddAsync(name);
            }

            // The picker shows the category it now holds rather than what was typed: the new one, or the one put back.
            _showText(_current() is { } category ? _nameOf(category) : string.Empty);
        }
        catch (Exception ex)
        {
            // Nothing awaits this: it's the picker's, from the keyboard or the focus leaving.
            _showFailure(ex);
        }
        finally
        {
            _isCommitting = false;
        }
    }

    // Saves the category and picks it. A refused name, or one saved but not read back, puts back the category the picker had.
    private async Task AddAsync(string name)
    {
        TCategory? category = null;
        await _runSafely(async () => category = await _addAsync(name));
        if (category is null)
        {
            _pick(_before);
            return;
        }

        _pick(category);
        _added($"{_nameOf(category)} was added.");
    }

    private TCategory? Listed(string name) => _options().FirstOrDefault(c => IsNamed(c, name));

    private bool IsNamed(TCategory? category, string name) =>
        category is not null && string.Equals(_nameOf(category), name, StringComparison.OrdinalIgnoreCase);
}
