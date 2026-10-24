using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Vim;

/// <summary>What a command typed after <c>:</c> asks for.</summary>
public enum VimCommandKind
{
    Save,
    New,
    Delete,
    Open,
    Revert,
    Screen,
    Settings,
    Help,
}

/// <summary>A command read from the command line: what it asks for, the name typed after it, and the screen it shows.</summary>
public sealed record VimCommand(VimCommandKind Kind, string Text = "", AppScreen Screen = default);

/// <summary>
/// One command the command line knows: its name, how short it may be typed (as Vim's own, <c>:w</c> is
/// <c>:write</c>), and how the cheat sheet shows it.
/// </summary>
public sealed record VimCommandDefinition(string Name, int ShortestLength, VimCommandKind Kind, AppScreen Screen, bool TakesText, string Usage, string Description)
{
    /// <summary>Whether <paramref name="typed"/> names this command: its name, or the start of it at least as long as allowed.</summary>
    public bool IsNamedBy(string typed) =>
        typed.Length >= ShortestLength && typed.Length <= Name.Length && Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Reads what's typed after <c>:</c> into a command, or says why it can't.</summary>
public static class VimCommandLine
{
    public static IReadOnlyList<VimCommandDefinition> Commands { get; } =
    [
        new("write", 1, VimCommandKind.Save, default, false, ":w", "Save"),
        new("new", 3, VimCommandKind.New, default, false, ":new", "Start a new one"),
        new("delete", 3, VimCommandKind.Delete, default, false, ":delete", "Delete the one open, asking first"),
        new("edit", 1, VimCommandKind.Open, default, true, ":e name", "Open the one whose name matches, from the screen's list"),
        new("revert", 3, VimCommandKind.Revert, default, false, ":revert", "Throw away the changes since the last Save (Products)"),
        new("raw", 2, VimCommandKind.Screen, AppScreen.RawMaterials, false, ":raw", "Raw Materials"),
        new("sub", 3, VimCommandKind.Screen, AppScreen.SubAssemblies, false, ":sub", "Sub-Assemblies"),
        new("products", 3, VimCommandKind.Screen, AppScreen.Products, false, ":products", "Products"),
        new("reports", 3, VimCommandKind.Screen, AppScreen.Reports, false, ":reports", "Reports"),
        new("plan", 2, VimCommandKind.Screen, AppScreen.ProductionPlan, false, ":plan", "Production Plan"),
        new("simulator", 2, VimCommandKind.Screen, AppScreen.PriceSimulator, false, ":simulator", "Price Simulator"),
        new("print", 3, VimCommandKind.Screen, AppScreen.Print, false, ":print", "Print what Products has open"),
        new("settings", 3, VimCommandKind.Settings, default, false, ":settings", "Settings"),
        new("help", 1, VimCommandKind.Help, default, false, ":help", "These keys"),
    ];

    /// <summary>
    /// The command <paramref name="line"/> asks for, or null with <paramref name="error"/> saying why: nothing typed, a
    /// name that's no command, or a command missing the name it needs (or given one it doesn't take).
    /// </summary>
    public static VimCommand? Parse(string line, out string error)
    {
        var text = line.Trim().TrimStart(':').TrimStart();
        var split = text.IndexOfAny([' ', '\t']);
        var name = split < 0 ? text : text[..split];
        var argument = split < 0 ? string.Empty : text[(split + 1)..].Trim();
        error = string.Empty;

        if (name.Length == 0)
        {
            error = "Type a command: :w saves, :e name opens one, :help lists them all.";
            return null;
        }

        if (Commands.FirstOrDefault(command => command.IsNamedBy(name)) is not { } definition)
        {
            error = $"Not a command: {name}. :help lists them all.";
            return null;
        }

        if (definition.TakesText && argument.Length == 0)
        {
            error = $"Say which one: {definition.Usage}";
            return null;
        }

        if (!definition.TakesText && argument.Length > 0)
        {
            error = $"{definition.Usage} takes nothing after it.";
            return null;
        }

        return new VimCommand(definition.Kind, argument, definition.Screen);
    }
}
