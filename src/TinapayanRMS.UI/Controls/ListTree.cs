using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Syncfusion.UI.Xaml.TreeView;
using Syncfusion.UI.Xaml.TreeView.Engine;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// What the main screens' lists (ProductListCard, MaterialListCard, SubAssemblyListCard) do with their SfTreeView, for a
/// screen that's an <see cref="IListTreeScreen"/>; set on the tree as <c>controls:ListTree.IsEnabled="True"</c>.
/// <list type="bullet">
/// <item>A row picked from elsewhere (Back and Forward, a Save, a link from another screen, a new folder) is brought into
/// view, the folders above it opened: the tree doesn't bring a selection set from its SelectedItems into view itself. A
/// row clicked is in view already, so this leaves it where it is.</item>
/// <item>Records can be dragged, one or several, onto a folder, or beside another record in that one's folder, and the
/// screen moves them there (<see cref="IListTreeScreen.Drop"/>), as Move To does. A drop that would leave them all where
/// they are is refused, as is anything dragged in from outside the list. The tree never moves a row itself.</item>
/// <item>A right-click opens the row's menu, or in the empty space the list's own (<see cref="IListTreeScreen.TreeMenuFor"/>);
/// from the keyboard (the menu key, or Shift+F10), the picked row's, beneath it.</item>
/// <item>F2 and Del run what the picked row's menu shows beside them, and Enter opens or closes a folder, as a click does.</item>
/// </list>
/// </summary>
public static class ListTree
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(ListTree), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfTreeView tree || e.NewValue is not true)
        {
            return;
        }

        DependencyPropertyDescriptor.FromProperty(SfTreeView.SelectedItemProperty, typeof(SfTreeView))
            .AddValueChanged(tree, (_, _) =>
            {
                if (tree.SelectedItem is { } node)
                {
                    ShowRow(tree, node);
                }
            });
        tree.ItemDragStarting += ItemDragStarting;
        tree.ItemDragOver += ItemDragOver;
        tree.ItemDropping += ItemDropping;
        tree.ItemContextMenuOpening += ItemContextMenuOpening;
        tree.ContextMenuOpening += ContextMenuOpening;
        tree.KeyDown += KeyDown;
    }

    private static IListTreeScreen? ScreenOf(object? tree) => (tree as FrameworkElement)?.DataContext as IListTreeScreen;

    private static void ItemDragStarting(object? sender, TreeViewItemDragStartingEventArgs e) =>
        e.Cancel = ScreenOf(sender) is not { } screen || !screen.CanDrag(e.DraggingNodes.Select(node => node.Content));

    /// <summary>Shows where a drop files the records: into a folder, highlighted whole, or beside another record in that one's folder.</summary>
    private static void ItemDragOver(object? sender, TreeViewItemDragOverEventArgs e)
    {
        if (ScreenOf(sender) is not { } screen || !CanDrop(screen, e.DraggingNodes, e.TargetNode))
        {
            e.DropPosition = DropPosition.None;
        }
        else if (e.TargetNode.Content is IListFolder)
        {
            e.DropPosition = DropPosition.DropAsChild;
        }
        else if (e.DropPosition is DropPosition.DropAsChild or DropPosition.None)
        {
            // A record holds nothing: over the middle of one, the line goes under it.
            e.DropPosition = DropPosition.DropBelow;
        }
    }

    /// <summary>Moves the records through the screen, which saves the move and files them; the folder the first lands in opens, so it's seen arriving.</summary>
    private static void ItemDropping(object? sender, TreeViewItemDroppingEventArgs e)
    {
        e.Handled = true;
        if (sender is not SfTreeView tree || ScreenOf(tree) is not { } screen || !CanDrop(screen, e.DraggingNodes, e.TargetNode))
        {
            return;
        }

        screen.Drop([.. e.DraggingNodes.Select(node => node.Content)], e.TargetNode?.Content).Subscribe(row =>
        {
            if (row is not null)
            {
                ShowRow(tree, row);
            }
        });
    }

    private static bool CanDrop(IListTreeScreen screen, IList<TreeViewNode>? dragging, TreeViewNode? target) =>
        dragging is { Count: > 0 } && screen.CanDrop(dragging.Select(node => node.Content), target?.Content);

    /// <summary>
    /// A row right-clicked: its menu, for it and, when it was one of the rows picked, for the rest of them too, as in a file
    /// explorer. A row outside the selection is picked alone first, so the record the menu opens on is the one showing.
    /// </summary>
    private static void ItemContextMenuOpening(object? sender, ItemContextMenuOpeningEventArgs e)
    {
        if (sender is not SfTreeView tree || ScreenOf(tree) is not { } screen || e.MenuInfo.Node?.Content is not { } node)
        {
            e.Cancel = true;
            return;
        }

        if (!screen.SelectedTreeNodes.Contains(node))
        {
            screen.SelectedTreeNode = node;
        }

        // A row with nothing to offer, such as Products' Deleted folder, whose products have menus of their own, opens none.
        if (screen.TreeMenuFor(node) is not { Count: > 0 } items)
        {
            e.Cancel = true;
            return;
        }

        e.ContextMenu = TreeContextMenu.Build(tree, items);
    }

    /// <summary>
    /// The list's own menu, which opens where a right-click isn't on a row. From the keyboard (the menu key, or Shift+F10)
    /// it's the picked row's menu instead, beneath it. A row's own right-click never gets here: the tree has shown its menu
    /// and handled the event.
    /// </summary>
    private static void ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not SfTreeView tree || ScreenOf(tree) is not { } screen)
        {
            e.Handled = true;
            return;
        }

        // Opened here rather than by the service, which places a menu it opens from the keyboard itself, whatever it's
        // given: over the row rather than under it.
        if (e.CursorLeft < 0 && e.CursorTop < 0 && screen.SelectedTreeNode is { } node)
        {
            e.Handled = true;
            if (screen.TreeMenuFor(node) is { Count: > 0 } items)
            {
                var rowMenu = TreeContextMenu.Build(tree, items);
                rowMenu.PlacementTarget = TreeContextMenu.RowOf(tree, node) ?? (UIElement)tree;
                rowMenu.Placement = PlacementMode.Bottom;
                rowMenu.IsOpen = true;
            }

            return;
        }

        tree.ContextMenu = TreeContextMenu.Build(tree, screen.TreeMenuFor(null));
    }

    private static void KeyDown(object sender, KeyEventArgs e)
    {
        if (ScreenOf(sender) is not { } screen || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Enter && screen.SelectedTreeNodes is [IListFolder folder])
        {
            folder.IsExpanded = !folder.IsExpanded;
            e.Handled = true;
            return;
        }

        var gesture = e.Key switch
        {
            Key.F2 => "F2",
            Key.Delete => "Del",
            _ => null,
        };
        if (gesture is not null && screen.TreeShortcut(gesture) is { Command: { } command } item && command.CanExecute(item.Parameter))
        {
            command.Execute(item.Parameter);
            e.Handled = true;
        }
    }

    /// <summary>Opens the folders above <paramref name="node"/> and scrolls it into view, if it isn't already.</summary>
    private static void ShowRow(SfTreeView tree, object node) =>
        tree.BringIntoView(node, disableAnimation: true, canExpand: true, ScrollToPosition.MakeVisible);
}
