using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.TreeView;
using Syncfusion.UI.Xaml.TreeView.Engine;
using TinapayanRMS.Presentation.ViewModels;
using TreeViewItem = Syncfusion.UI.Xaml.TreeView.TreeViewItem;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The right-click menus of the folder trees (the Products, Raw Materials and Sub-Assemblies lists): a menu built from the items a
/// screen describes (TreeMenuItem), and the row a menu opened from the keyboard is placed under.
/// </summary>
public static class TreeContextMenu
{
    /// <summary>
    /// A menu of <paramref name="items"/>, themed as <paramref name="owner"/>'s window is: a menu opens in a window of its
    /// own, which the theme only reaches when it's given it, as a dialog is.
    /// </summary>
    public static ContextMenu Build(FrameworkElement owner, IReadOnlyList<TreeMenuItem> items)
    {
        var menu = new ContextMenu();
        if (Window.GetWindow(owner) is { } window)
        {
            SfSkinManager.SetTheme(menu, SfSkinManager.GetTheme(window));
        }

        foreach (var item in items)
        {
            menu.Items.Add(ToMenuItem(owner, item));
        }

        return menu;
    }

    private static Control ToMenuItem(FrameworkElement owner, TreeMenuItem item)
    {
        if (item.IsSeparator)
        {
            return new Separator();
        }

        var menuItem = new MenuItem
        {
            // An underscore in a name would otherwise be taken for an access key's marker.
            Header = item.Header.Replace("_", "__", StringComparison.Ordinal),
            Command = item.Command,
            CommandParameter = item.Parameter,
            InputGestureText = item.Gesture,
            IsEnabled = item.IsEnabled,
        };

        if (item.Glyph is not null)
        {
            menuItem.Icon = new TextBlock { Text = item.Glyph, Style = (Style)owner.FindResource("IconGlyphStyle") };
        }

        foreach (var child in item.Items)
        {
            menuItem.Items.Add(ToMenuItem(owner, child));
        }

        return menuItem;
    }

    /// <summary>The row showing <paramref name="node"/>, or null while it's scrolled out of sight or in a closed folder.</summary>
    public static TreeViewItem? RowOf(SfTreeView tree, object node)
    {
        return Find(tree);

        TreeViewItem? Find(DependencyObject parent)
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TreeViewItem row && (row.DataContext == node || row.DataContext is TreeViewNode { Content: var content } && content == node))
                {
                    return row;
                }

                if (Find(child) is { } found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
