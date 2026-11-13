using System.Collections.ObjectModel;
using System.ComponentModel;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.Presentation.ViewModels;

/// <summary>
/// A screen whose list is a tree of folders: Products, Raw Materials and Sub-Assemblies. What its list card works the tree
/// with (ProductList, MaterialList and SubAssemblyList, through FolderTree and MoveNoticeBar): the rows picked, their menus
/// and shortcuts, dragging rows onto a folder, and the notice of the last move. The rows are the screen's own tree's nodes.
/// The drag rules are the screen's alone: the cards ask these members, so the rules the tests check are the ones the
/// window runs.
/// </summary>
public interface IListTreeScreen : INotifyPropertyChanged
{
    /// <summary>The rows picked, bound to the tree's SelectedItems. The first is <see cref="SelectedTreeNode"/>.</summary>
    ObservableCollection<object> SelectedTreeNodes { get; }

    /// <summary>The row picked first, or null for none; setting it picks it alone.</summary>
    object? SelectedTreeNode { get; set; }

    /// <summary>What the last move did, for its notice with Undo; null while none shows.</summary>
    string? MoveNotice { get; }

    /// <summary>Puts the notice of the last move away.</summary>
    ReactiveCommand<RxVoid, RxVoid> DismissMoveNoticeCommand { get; }

    /// <summary>The menu for a row, or with null for the list's empty space; empty when there's nothing to offer.</summary>
    IReadOnlyList<TreeMenuItem> TreeMenuFor(object? node);

    /// <summary>The item of the picked row's menu with <paramref name="gesture"/> beside it ("F2", "Del"), or null for none.</summary>
    TreeMenuItem? TreeShortcut(string gesture);

    /// <summary>
    /// Whether a drag can take <paramref name="node"/>'s row, with the other rows picked that it can take: the records, and
    /// on Products one of the user's folders. Which of them go together is <see cref="CanDrop"/>'s to say.
    /// </summary>
    bool CanDrag(object node);

    /// <summary>Whether dropping <paramref name="nodes"/> on <paramref name="target"/> (null for nothing) would move any of them.</summary>
    bool CanDrop(IEnumerable<object> nodes, object? target);

    /// <summary>
    /// Moves <paramref name="nodes"/> where dropping them on <paramref name="target"/> files them, as Move To does: saved at
    /// once, with Undo. Returns the row of the first moved, to bring into view, or null when none moved. A drop made while
    /// an earlier move is still saving does nothing, as a click on a busy button does: run together, the second would be
    /// judged against the list as it was, and each would take the other's Undo.
    /// </summary>
    Task<object?> DropAsync(IEnumerable<object> nodes, object? target);
}
