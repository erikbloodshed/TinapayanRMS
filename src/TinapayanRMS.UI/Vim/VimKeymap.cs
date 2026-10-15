namespace TinapayanRMS.UI.Vim;

/// <summary>What a Vim key asks for. The same action reads differently by where the cursor is: see <see cref="VimKeys"/>.</summary>
public enum VimAction
{
    None,
    Left,
    Down,
    Up,
    Right,
    First,
    Last,
    LineStart,
    LineEnd,
    Insert,
    Append,
    InsertAtStart,
    AppendAtEnd,
    Change,
    Open,
    OpenAbove,
    Delete,
    Rename,
    NextTab,
    PreviousTab,
    Back,
    Forward,
    Undo,
    Redo,
    Search,
    NextControl,
    PreviousControl,
    Hints,
    CommandLine,
    CheatSheet,
}

/// <summary>Where the cursor can be, as far as the keys are concerned.</summary>
[Flags]
public enum VimContexts
{
    None = 0,

    /// <summary>A text box, number box or picker.</summary>
    Field = 1,

    /// <summary>A grid, the Scaling tab's tree grid, or the menu: the arrow keys move a row or a cell.</summary>
    Table = 2,

    /// <summary>The Products and Raw Materials lists, whose rows are folders and records.</summary>
    Tree = 4,

    /// <summary>A button, a tab, a check box.</summary>
    Other = 8,

    Anywhere = Field | Table | Tree | Other,
}

/// <summary>
/// One key or two-key sequence and what it does where. <paramref name="Keys"/> typed as text is one or two characters;
/// a named key (Esc, Ctrl+R) is longer, and <see cref="VimKeys"/> handles it itself.
/// </summary>
public sealed record VimKeyBinding(string Keys, VimAction Action, VimContexts Where, string Description)
{
    /// <summary>Whether the keys are typed as text, which is how <see cref="VimKeySequence"/> reads them.</summary>
    public bool IsTyped => Keys.Length is 1 or 2;
}

/// <summary>
/// Every Vim key the app knows, where it works and what it does there: the one table the keys are read from
/// (<see cref="VimKeySequence"/>) and the cheat sheet is drawn from, so neither can drift from the other.
/// </summary>
public static class VimKeymap
{
    private const VimContexts Lists = VimContexts.Table | VimContexts.Tree;
    private const VimContexts Controls = VimContexts.Field | VimContexts.Other;

    public static IReadOnlyList<VimKeyBinding> Bindings { get; } =
    [
        // In a list: a grid, a tree, the menu.
        new("h", VimAction.Left, VimContexts.Table, "Left a cell"),
        new("j", VimAction.Down, Lists, "Down a row"),
        new("k", VimAction.Up, Lists, "Up a row"),
        new("l", VimAction.Right, VimContexts.Table, "Right a cell"),
        new("h", VimAction.Left, VimContexts.Tree, "Close the folder, or go up to its folder"),
        new("l", VimAction.Right, VimContexts.Tree, "Open the folder"),
        new("gg", VimAction.First, Lists, "First row"),
        new("G", VimAction.Last, Lists, "Last row"),
        new("0", VimAction.LineStart, VimContexts.Table, "First column"),
        new("$", VimAction.LineEnd, VimContexts.Table, "Last column"),
        new("i", VimAction.Insert, VimContexts.Table, "Type in the cell, from its start"),
        new("a", VimAction.Append, VimContexts.Table, "Type in the cell, from its end"),
        new("I", VimAction.InsertAtStart, VimContexts.Table, "Type in the row's first column"),
        new("A", VimAction.AppendAtEnd, VimContexts.Table, "Type in the row's last column"),
        new("cc", VimAction.Change, VimContexts.Table, "Type over the cell"),
        new("o", VimAction.Open, VimContexts.Table, "Type a new line, in the empty row at the bottom"),
        new("O", VimAction.OpenAbove, VimContexts.Table, "Type a new line, put in above this one (ingredient lines)"),
        new("dd", VimAction.Delete, VimContexts.Table, "Delete the line"),
        new("o", VimAction.Open, VimContexts.Tree, "Open or close the folder"),
        new("dd", VimAction.Delete, VimContexts.Tree, "Delete the row, as its menu's Delete does"),
        new("r", VimAction.Rename, VimContexts.Tree, "Rename the row, as its menu's Rename does"),
        new("J", VimAction.NextControl, Lists, "Leave the list, for the next control"),
        new("K", VimAction.PreviousControl, Lists, "Leave the list, for the control before it"),

        // In a field.
        new("j", VimAction.Down, VimContexts.Field, "Next field, as Tab"),
        new("k", VimAction.Up, VimContexts.Field, "Previous field, as Shift+Tab"),
        new("h", VimAction.Left, VimContexts.Field, "Caret left"),
        new("l", VimAction.Right, VimContexts.Field, "Caret right"),
        new("0", VimAction.LineStart, VimContexts.Field, "Caret to the start"),
        new("$", VimAction.LineEnd, VimContexts.Field, "Caret to the end"),
        new("i", VimAction.Insert, VimContexts.Field, "Type at the caret"),
        new("a", VimAction.Append, VimContexts.Field, "Type after the caret"),
        new("I", VimAction.InsertAtStart, VimContexts.Field, "Type at the start"),
        new("A", VimAction.AppendAtEnd, VimContexts.Field, "Type at the end"),
        new("cc", VimAction.Change, VimContexts.Field, "Type over the field"),

        // On a button, a tab, a check box.
        new("j", VimAction.Down, VimContexts.Other, "Next control, as Tab"),
        new("k", VimAction.Up, VimContexts.Other, "Previous control, as Shift+Tab"),
        new("h", VimAction.Left, VimContexts.Other, "Left, as the arrow key (the tab before, on a tab)"),
        new("l", VimAction.Right, VimContexts.Other, "Right, as the arrow key (the next tab, on a tab)"),
        new("J", VimAction.NextControl, Controls, "Next control"),
        new("K", VimAction.PreviousControl, Controls, "Previous control"),

        // Anywhere.
        new("Esc", VimAction.None, VimContexts.Anywhere, "Stop typing, keeping what's typed; or drop a half-typed key"),
        new("Ctrl+[", VimAction.None, VimContexts.Anywhere, "Esc, as in Vim"),
        new("Ctrl+N", VimAction.None, Controls | VimContexts.Table, "Down an open dropdown or suggestion list, as in Vim"),
        new("Ctrl+P", VimAction.None, Controls | VimContexts.Table, "Up an open dropdown or suggestion list, as in Vim"),
        new("f", VimAction.Hints, VimContexts.Anywhere, "Label what can be clicked; type a label to click it"),
        new("/", VimAction.Search, VimContexts.Anywhere, "Search the screen's list"),
        new("gt", VimAction.NextTab, VimContexts.Anywhere, "Next tab"),
        new("gT", VimAction.PreviousTab, VimContexts.Anywhere, "Previous tab"),
        new("H", VimAction.Back, VimContexts.Anywhere, "Back a screen, as Alt+Left"),
        new("L", VimAction.Forward, VimContexts.Anywhere, "Forward a screen, as Alt+Right"),
        new("u", VimAction.Undo, VimContexts.Anywhere, "Undo, as Ctrl+Z"),
        new("Ctrl+R", VimAction.Redo, VimContexts.Anywhere, "Redo, as Ctrl+Y"),
        new(":", VimAction.CommandLine, VimContexts.Anywhere, "Type a command (Save, open by name…)"),
        new("?", VimAction.CheatSheet, VimContexts.Anywhere, "These keys"),
    ];

    /// <summary>The keys that work in <paramref name="context"/>, those for it alone before those that work anywhere.</summary>
    public static IEnumerable<VimKeyBinding> For(VimContexts context) =>
        Bindings.Where(binding => binding.Where != VimContexts.Anywhere && (binding.Where & context) != 0)
            .Concat(Bindings.Where(binding => binding.Where == VimContexts.Anywhere));
}
