using System.Collections.ObjectModel;
using System.ComponentModel;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.ViewModels;

/// <summary>
/// A screen whose list is a tree of folders: Products, Raw Materials and Sub-Assemblies. What its list card works the tree
/// with (Controls/ListTree): the rows picked, their menus and shortcuts, dragging rows onto a folder, and the notice of the
/// last move. The rows are the screen's own tree's nodes.
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

    /// <summary>Whether <paramref name="nodes"/> can be dragged: records alone, never a folder.</summary>
    bool CanDrag(IEnumerable<object> nodes);

    /// <summary>Whether dropping <paramref name="nodes"/> on <paramref name="target"/> (null for nothing) would move any of them.</summary>
    bool CanDrop(IEnumerable<object> nodes, object? target);

    /// <summary>
    /// Moves <paramref name="nodes"/> where dropping them on <paramref name="target"/> files them, as Move To does: saved at
    /// once, with Undo. Completes with the row of the first moved, to bring into view, or null when none moved.
    /// </summary>
    IObservable<object?> Drop(IEnumerable<object> nodes, object? target);
}
