using System.Windows.Input;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// One item of the menu a right-click opens on the Products list (ProductsViewModel.TreeMenuFor), which ProductListCard
/// turns into a MenuItem: what it reads, the command it runs and what with, the key that runs it too, and a submenu in
/// place of a command.
/// </summary>
public sealed record ProductTreeMenuItem(string Header, ICommand? Command = null, object? Parameter = null)
{
    /// <summary>The line between groups of items.</summary>
    public static ProductTreeMenuItem Separator { get; } = new("-");

    public bool IsSeparator => ReferenceEquals(this, Separator);

    /// <summary>The key that does the same with the row picked, shown at the item's right: "F2", "Del".</summary>
    public string? Gesture { get; init; }

    /// <summary>The Segoe Fluent Icons glyph before the header, or null for none.</summary>
    public string? Glyph { get; init; }

    /// <summary>False for an item that's there to show what can't be done here: moving a product to the folder it's in.</summary>
    public bool IsEnabled { get; init; } = true;

    public IReadOnlyList<ProductTreeMenuItem> Items { get; init; } = [];
}
