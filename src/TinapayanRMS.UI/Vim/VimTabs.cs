using System.Windows;
using System.Windows.Controls;

namespace TinapayanRMS.UI.Vim;

/// <summary><c>gt</c> and <c>gT</c> on a window's tabs.</summary>
internal static class VimTabs
{
    /// <summary>
    /// Shows the next visible tab of <paramref name="tabs"/> (+1) or the one before (-1), round from the last to the
    /// first, and puts the keyboard on its header, as a click on it does; <c>j</c> goes on into the screen from there. A
    /// closed tab (the main window's are hidden, not removed) is passed over.
    /// </summary>
    public static void Step(TabControl tabs, int step)
    {
        var visible = tabs.Items.OfType<TabItem>().Where(tab => tab.Visibility == Visibility.Visible).ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var at = tabs.SelectedItem is TabItem selected ? visible.IndexOf(selected) : -1;
        var next = visible[(((at < 0 ? 0 : at + step) % visible.Count) + visible.Count) % visible.Count];
        tabs.SelectedItem = next;
        next.Focus();
    }
}
