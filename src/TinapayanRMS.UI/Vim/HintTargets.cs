using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;
using Syncfusion.UI.Xaml.TreeGrid;
using Syncfusion.UI.Xaml.TreeView;
using Syncfusion.Windows.Tools.Controls;

namespace TinapayanRMS.UI.Vim;

/// <summary>Something a link hint can act on: where its label goes, and what typing the label does.</summary>
internal sealed record HintTarget(Point Position, Action Activate);

/// <summary>
/// Finds what a link hint can act on in a window's content: the buttons, fields, pickers, check boxes and tabs on show,
/// the rows of its lists and grids that are in view, and the menu's rows. It walks only what's visible — a hidden tab,
/// a closed menu and anything scrolled out of view are passed over — and a grid or list counts as one stop: its rows are
/// read from what it has realized or from its row axis, never from its cells.
/// </summary>
internal static class HintTargets
{
    // ButtonBase.OnClick raises Click and runs the Command, as a mouse click does. It's protected; ButtonAdv's automation
    // peer, which would otherwise be the way in, offers no Invoke.
    private static readonly MethodInfo OnClick = typeof(ButtonBase).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    // A SplitButtonAdv's own click (its Click and its Command), protected too. It leaves IsPressed set, which the mouse's
    // release would clear.
    private static readonly MethodInfo SplitClick = typeof(SplitButtonAdv).GetMethod("OnClick", BindingFlags.Instance | BindingFlags.NonPublic)!;

    public static List<HintTarget> In(FrameworkElement root)
    {
        var targets = new List<HintTarget>();
        Visit(root, root, new Rect(root.RenderSize), targets);
        return targets;
    }

    private static void Visit(DependencyObject node, FrameworkElement root, Rect clip, List<HintTarget> targets)
    {
        if (node is UIElement element)
        {
            if (!element.IsVisible || !element.IsEnabled || element is ScrollBar)
            {
                return;
            }

            // What a scroll viewer or a clipping panel cuts off can't be clicked, so it gets no label.
            if (element is ScrollContentPresenter || element.ClipToBounds)
            {
                clip = Rect.Intersect(clip, BoundsIn(element, root));
                if (clip.IsEmpty)
                {
                    return;
                }
            }

            if (TryAdd(element, root, clip, targets))
            {
                return;
            }
        }

        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            Visit(VisualTreeHelper.GetChild(node, i), root, clip, targets);
        }
    }

    /// <summary>Adds <paramref name="element"/>'s targets if it's a kind that has them; true when it is, so what's inside
    /// it (a picker's button, a number box's spinners) gets none of its own.</summary>
    private static bool TryAdd(UIElement element, FrameworkElement root, Rect clip, List<HintTarget> targets)
    {
        switch (element)
        {
            case SfDataGrid grid:
                AddGridRows(grid, root, clip, targets);
                return true;
            case SfTreeView tree:
                AddTreeRows(tree, root, clip, targets);
                return true;
            case SfTreeGrid treeGrid:
                Add(treeGrid, root, clip, targets, () => treeGrid.Focus());
                return true;
            case TextBoxBase box:
                Add(box, root, clip, targets, () => box.Focus());
                return true;
            case ComboBoxAdv picker:
                Add(picker, root, clip, targets, () => VimKeys.FocusPicker(picker));
                return true;
            case ComboBox comboBox:
                Add(comboBox, root, clip, targets, () => comboBox.Focus());
                return true;
            case TabItem tab:
                Add(tab, root, clip, targets, () =>
                {
                    tab.IsSelected = true;
                    tab.Focus();
                });
                return true;
            case ListBoxItem row:
                // The menu's rows go where they lead on Enter (MainWindow's NavigationMenu_PreviewKeyDown).
                Add(row, root, clip, targets, () =>
                {
                    row.Focus();
                    VimKeys.RaiseKey(row, Key.Enter);
                });
                return true;
            case ButtonBase button:
                Add(button, root, clip, targets, () => Click(button));
                return true;
            case SplitButtonAdv split:
                // Its button, as a click on it (Products' New); the arrow beside it opens its list.
                Add(split, root, clip, targets, () =>
                {
                    Focus(split);
                    SplitClick.Invoke(split, null);
                    split.SetValue(DropDownButtonAdv.IsPressedProperty, false);
                });
                return true;
            case DropDownButtonAdv dropDown:
                Add(dropDown, root, clip, targets, () =>
                {
                    Focus(dropDown);
                    dropDown.IsDropDownOpen = true;
                });
                return true;
            default:
                return false;
        }
    }

    private static void Add(UIElement element, FrameworkElement root, Rect clip, List<HintTarget> targets, Action activate)
    {
        var shown = Rect.Intersect(BoundsIn(element, root), clip);
        if (!shown.IsEmpty)
        {
            targets.Add(new HintTarget(shown.TopLeft, activate));
        }
    }

    /// <summary>A label on each grid row in view (the add row too), or on the grid itself when it shows none.</summary>
    private static void AddGridRows(SfDataGrid grid, FrameworkElement root, Rect clip, List<HintTarget> targets)
    {
        var shown = Rect.Intersect(BoundsIn(grid, root), clip);
        if (shown.IsEmpty)
        {
            return;
        }

        var before = targets.Count;
        if (grid.GetVisualContainer() is { } container)
        {
            var first = grid.GetFirstDataRowIndex();
            var last = grid.GetLastDataRowIndex();
            var addRow = grid.AddNewRowPosition != AddNewRowPosition.None ? grid.GetAddNewRowIndex() : -1;
            var toRoot = container.TransformToAncestor(root);
            foreach (var line in container.ScrollRows.GetVisibleLines())
            {
                var row = line.LineIndex;
                if (!(first >= 0 && row >= first && row <= last) && row != addRow)
                {
                    continue;
                }

                var top = toRoot.Transform(new Point(0, line.ClippedOrigin));
                var rowShown = Rect.Intersect(new Rect(top.X, top.Y, container.ActualWidth, Math.Max(1, line.ClippedSize)), shown);
                if (!rowShown.IsEmpty)
                {
                    targets.Add(new HintTarget(rowShown.TopLeft, () => VimKeys.OpenGridRow(grid, row)));
                }
            }
        }

        if (targets.Count == before)
        {
            targets.Add(new HintTarget(shown.TopLeft, () => grid.Focus()));
        }
    }

    /// <summary>A label on each list row in view, or on the list itself when it shows none.</summary>
    private static void AddTreeRows(SfTreeView tree, FrameworkElement root, Rect clip, List<HintTarget> targets)
    {
        var shown = Rect.Intersect(BoundsIn(tree, root), clip);
        if (shown.IsEmpty)
        {
            return;
        }

        var before = targets.Count;
        AddRealizedRows(tree, tree, root, shown, targets);
        if (targets.Count == before)
        {
            targets.Add(new HintTarget(shown.TopLeft, () => tree.Focus()));
        }
    }

    private static void AddRealizedRows(DependencyObject node, SfTreeView tree, FrameworkElement root, Rect shown, List<HintTarget> targets)
    {
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is UIElement { IsVisible: false })
            {
                continue;
            }

            if (child is Syncfusion.UI.Xaml.TreeView.TreeViewItem { TreeViewItemInfo.Node.Content: { } item } row)
            {
                var rowShown = Rect.Intersect(BoundsIn(row, root), shown);
                if (!rowShown.IsEmpty)
                {
                    targets.Add(new HintTarget(rowShown.TopLeft, () => VimKeys.OpenTreeRow(tree, item)));
                }

                continue;
            }

            AddRealizedRows(child, tree, root, shown, targets);
        }
    }

    /// <summary>Clicks <paramref name="button"/> as the mouse does: the keyboard to it, if it takes the keyboard, then the click.</summary>
    private static void Click(ButtonBase button)
    {
        Focus(button);
        OnClick.Invoke(button, null);
    }

    private static void Focus(UIElement element)
    {
        if (element.Focusable)
        {
            element.Focus();
        }
    }

    private static Rect BoundsIn(UIElement element, FrameworkElement root) =>
        element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
}
