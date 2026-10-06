using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using ReactiveUI;
using Syncfusion.UI.Xaml.TreeView;
using Syncfusion.UI.Xaml.TreeView.Engine;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// The Products list: the products in a folder for their stage, then their category, with a search box. Dragging products
/// onto a folder moves them there; a right-click opens a menu of what can be done with the row, or with the list; F2, Del
/// and Enter work on the row picked. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class ProductListCard : UserControl
{
    /// <summary>How long the notice of a move shows before hiding itself; checked again later while the mouse is over it.</summary>
    private readonly DispatcherTimer _moveNoticeTimer = new() { Interval = TimeSpan.FromSeconds(8) };

    public ProductListCard()
    {
        InitializeComponent();

        // A row picked from elsewhere (Sub-Assemblies' Used By tiles, a Save, a clone, a new category's folder) may be in a
        // closed folder or scrolled out of sight: the tree doesn't bring a selection set from its SelectedItems into view
        // itself. A row clicked is in view already, so this leaves it where it is.
        DependencyPropertyDescriptor.FromProperty(SfTreeView.SelectedItemProperty, typeof(SfTreeView))
            .AddValueChanged(Tree, (_, _) =>
            {
                if (Tree.SelectedItem is ProductTreeNode node)
                {
                    ShowRow(node);
                }
            });

        // A new notice starts the clock again; none stops it.
        DataContextChanged += (_, e) =>
        {
            if (e.NewValue is ProductsViewModel viewModel)
            {
                viewModel.WhenAnyValue(x => x.MoveNotice).Subscribe(notice =>
                {
                    _moveNoticeTimer.Stop();
                    if (notice is not null)
                    {
                        _moveNoticeTimer.Start();
                    }
                });
            }
        };

        _moveNoticeTimer.Tick += (_, _) =>
        {
            // Kept while the mouse is over it, which may be on its way to Undo.
            if (!MoveNoticeBar.IsMouseOver && DataContext is ProductsViewModel viewModel)
            {
                _moveNoticeTimer.Stop();
                viewModel.DismissMoveNoticeCommand.Execute().Subscribe();
            }
        };
    }

    /// <summary>Only products can be dragged, one or several: the folders are fixed, and a deleted product only comes back by Restore.</summary>
    private void Tree_ItemDragStarting(object? sender, TreeViewItemDragStartingEventArgs e) =>
        e.Cancel = e.DraggingNodes.Any(node => node.Content is not ProductLeafNode { IsDeleted: false });

    /// <summary>
    /// Shows where a drop files the products: into a folder, highlighted whole, or beside another product in that one's
    /// folder. Refuses a drop that would leave them all where they are, and anything dragged in from outside the list.
    /// </summary>
    private void Tree_ItemDragOver(object? sender, TreeViewItemDragOverEventArgs e)
    {
        if (MovesFor(e.DraggingNodes, e.TargetNode).Count == 0)
        {
            e.DropPosition = DropPosition.None;
        }
        else if (e.TargetNode.Content is ProductFolderNode)
        {
            e.DropPosition = DropPosition.DropAsChild;
        }
        else if (e.DropPosition is DropPosition.DropAsChild or DropPosition.None)
        {
            // A product holds nothing: over the middle of one, the line goes under it.
            e.DropPosition = DropPosition.DropBelow;
        }
    }

    /// <summary>
    /// Moves the products through the Products screen, which saves the move and files them in their new folder; the tree
    /// never moves a row itself. The folder the first lands in opens, so it's seen arriving.
    /// </summary>
    private void Tree_ItemDropping(object? sender, TreeViewItemDroppingEventArgs e)
    {
        e.Handled = true;
        var moves = MovesFor(e.DraggingNodes, e.TargetNode);
        if (moves.Count == 0 || DataContext is not ProductsViewModel viewModel)
        {
            return;
        }

        viewModel.MoveProductsCommand.Execute(moves).Subscribe(_ =>
        {
            if (viewModel.ProductTree.Find(moves[0].ProductId) is { } leaf)
            {
                ShowRow(leaf);
            }
        });
    }

    /// <summary>What dropping the products dragged on <paramref name="target"/> would do: none for nothing.</summary>
    private static IReadOnlyList<ProductMove> MovesFor(IList<TreeViewNode>? dragging, TreeViewNode? target) =>
        dragging is { Count: > 0 } && dragging.All(node => node.Content is ProductLeafNode)
            ? ProductTree.MovesTo(target?.Content as ProductTreeNode, dragging.Select(node => ((ProductLeafNode)node.Content).Product))
            : [];

    /// <summary>
    /// A row right-clicked: its menu, for it and, when it was one of the rows picked, for the rest of them too, as in a
    /// file explorer. A row outside the selection is picked alone first, so the product it opens on is the one showing.
    /// </summary>
    private void Tree_ItemContextMenuOpening(object? sender, ItemContextMenuOpeningEventArgs e)
    {
        if (DataContext is not ProductsViewModel viewModel || e.MenuInfo.Node?.Content is not ProductTreeNode node)
        {
            e.Cancel = true;
            return;
        }

        if (!viewModel.SelectedTreeNodes.Contains(node))
        {
            viewModel.SelectedTreeNode = node;
        }

        // The Deleted folder has nothing to offer: its products have their own menus.
        if (viewModel.TreeMenuFor(node) is not { Count: > 0 } items)
        {
            e.Cancel = true;
            return;
        }

        e.ContextMenu = TreeContextMenu.Build(this, items);
    }

    /// <summary>
    /// The list's own menu, which opens where a right-click isn't on a row: New Product, New Category, Expand All and
    /// Collapse All. From the keyboard (the menu key, or Shift+F10) it's the menu of the row picked instead, beneath it.
    /// A row's own right-click never gets here: the tree has shown its menu and handled the event.
    /// </summary>
    private void Tree_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (DataContext is not ProductsViewModel viewModel)
        {
            e.Handled = true;
            return;
        }

        // Opened here rather than by the service, which places a menu it opens from the keyboard itself, whatever it's
        // given: over the row rather than under it.
        if (e.CursorLeft < 0 && e.CursorTop < 0 && viewModel.SelectedTreeNode is ProductTreeNode node)
        {
            e.Handled = true;
            if (viewModel.TreeMenuFor(node) is not { Count: > 0 } items)
            {
                return;
            }

            var rowMenu = TreeContextMenu.Build(this, items);
            rowMenu.PlacementTarget = TreeContextMenu.RowOf(Tree, node) ?? (UIElement)Tree;
            rowMenu.Placement = PlacementMode.Bottom;
            rowMenu.IsOpen = true;
            return;
        }

        Tree.ContextMenu = TreeContextMenu.Build(this, viewModel.TreeMenuFor(null));
    }

    /// <summary>
    /// F2 renames and Del deletes the row picked, product or category, as its menu shows; Enter opens or closes a folder,
    /// as clicking it does.
    /// </summary>
    private void Tree_KeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not ProductsViewModel viewModel || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Enter && viewModel.SelectedTreeNodes is [ProductFolderNode folder])
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
        if (gesture is not null && viewModel.TreeShortcut(gesture) is { Command: { } command } item && command.CanExecute(item.Parameter))
        {
            command.Execute(item.Parameter);
            e.Handled = true;
        }
    }

    /// <summary>Opens the folders above <paramref name="node"/> and scrolls it into view, if it isn't already.</summary>
    private void ShowRow(ProductTreeNode node) =>
        Tree.BringIntoView(node, disableAnimation: true, canExpand: true, ScrollToPosition.MakeVisible);
}
