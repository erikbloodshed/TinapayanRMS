namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// The naming in place a screen's list does, as in a file explorer, shared by Products, Raw Materials and Sub-Assemblies:
/// the row being named (<see cref="Edit"/>), and the rules for what's typed into its box. An empty name, or the name a row
/// had already, is an Esc; a second commit for the same box, as Enter's box is left, does nothing. What a name typed does,
/// and what Esc takes away, the screen says.
/// </summary>
public sealed class TreeNaming
{
    private readonly TreeSelection _selection;
    private readonly Action _changed;
    private readonly Func<object, bool> _isNewRow;
    private readonly Func<TreeNameEdit, string, string, Task> _commitAsync;
    private readonly Action<object> _cancelled;

    /// <param name="selection">The list's rows picked: the row being named is picked alone, and a new row taken out leaves them.</param>
    /// <param name="changed">Called as <see cref="Edit"/> changes, for the screen to raise the change of its TreeNameEdit.</param>
    /// <param name="isNewRow">
    /// Whether a row is a new record's or a new folder's: what its box starts with isn't a name it has, so typing it isn't an Esc.
    /// </param>
    /// <param name="commitAsync">
    /// What a name does, given the box it was typed into, the name, trimmed, and the text as typed, which a box brought back
    /// after a name is refused starts with.
    /// </param>
    /// <param name="cancelled">What Esc does to the row, besides closing its box: a new row goes, with nothing saved.</param>
    public TreeNaming(
        TreeSelection selection,
        Action changed,
        Func<object, bool> isNewRow,
        Func<TreeNameEdit, string, string, Task> commitAsync,
        Action<object> cancelled)
    {
        _selection = selection;
        _changed = changed;
        _isNewRow = isNewRow;
        _commitAsync = commitAsync;
        _cancelled = cancelled;
    }

    /// <summary>The row whose name is being typed, with what its box starts with, or null for none.</summary>
    public TreeNameEdit? Edit
    {
        get;
        set
        {
            if (!EqualityComparer<TreeNameEdit?>.Default.Equals(field, value))
            {
                field = value;
                _changed();
            }
        }
    }

    /// <summary>Picks <paramref name="node"/> alone, and puts a box on it starting with <paramref name="text"/>, selected.</summary>
    public void Start(object node, string text, string? hint = null)
    {
        _selection.Pick(node);
        Edit = new TreeNameEdit(node, text, hint);
    }

    /// <summary>
    /// The name typed into the box, on Enter or on leaving it. An empty name, or the name the row had (not a new row's), is
    /// an Esc (<see cref="Cancel"/>); any other closes the box and goes to the screen. A second call for the same box does nothing.
    /// </summary>
    public async Task CommitAsync(string text)
    {
        if (Edit is not { } edit)
        {
            return;
        }

        var name = text.Trim();
        if (name.Length == 0 || (!_isNewRow(edit.Node) && string.Equals(name, edit.InitialText, StringComparison.Ordinal)))
        {
            Cancel();
            return;
        }

        Edit = null;
        await _commitAsync(edit, name, text);
    }

    /// <summary>Esc in the box: it closes, the name stays as it was, and the screen takes away a new row.</summary>
    public void Cancel()
    {
        if (Edit is not { } edit)
        {
            return;
        }

        Edit = null;
        _cancelled(edit.Node);
    }

    /// <summary>
    /// Takes a new record's or folder's row, <paramref name="row"/>, out of the list (<paramref name="endInTree"/>, the
    /// tree's End…), and out of the rows picked, and stops the naming in it. Nothing for null, when there's no such row.
    /// </summary>
    public void EndNewRow(object? row, Action endInTree)
    {
        if (row is null)
        {
            return;
        }

        if (ReferenceEquals(Edit?.Node, row))
        {
            Edit = null;
        }

        endInTree();
        _selection.Quietly(() => _selection.Nodes.Remove(row));
    }
}
