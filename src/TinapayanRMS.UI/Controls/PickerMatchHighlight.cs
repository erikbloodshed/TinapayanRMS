using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Syncfusion.Windows.Tools.Controls;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Every picker that filters as you type (ComboBoxAdv, editable with Suggest, as App makes every one) highlights the name
/// its text best matches, and Enter or Tab picks it (2026-09-30, at the user's request: "auto highlight match item in all
/// the searchable combobox"). The picker only ever filtered its list: nothing in it was highlighted until an arrow key
/// was pressed, and Enter or Tab kept only a name typed out in full, so "flo" and Enter left the picker empty.
/// <list type="bullet">
/// <item>The match is the one the grids' drop-downs pick (ScreenAwareDropDownControl): the name typed in full, or else
/// the first starting with the text, or else the first containing it. "fir" highlights First Class Flour; "flour",
/// with no name starting with it, All Purpose Flour.</item>
/// <item>Enter or Tab picks the highlighted name, as a click on it would. Typing a name the list doesn't hold still
/// leaves nothing highlighted, so Category's prompt to add a new one still comes up.</item>
/// <item>Down and Up move on from the highlighted name. The picker's own arrow keys start from the top of the list
/// whatever is highlighted, so the first press would have landed on the name already there, or jumped back above it.
/// The key-ups that follow, up to the next key-down, are kept from the picker, which would refilter by the name written
/// into its box.</item>
/// <item>Moving the mouse onto another name takes the highlight off, as the picker does for its own pick, so only one
/// name is ever highlighted and Enter picks nothing the list doesn't show as highlighted.</item>
/// </list>
/// Class handlers rather than an attached property set as each picker loads: some pickers never raise Loaded (the
/// Products usage form's, see UsageFormPanel.xaml). A derived class's handlers run before ComboBoxAdv's own key
/// handling (its OnPreviewKeyDown and OnKeyUp, which UIElement's class handlers call), so Enter and Tab make the pick
/// before the picker settles its text, and the highlight waits until the picker has filtered its list and laid it out.
/// Registered once for the whole app, from App's static constructor.
/// </summary>
public static class PickerMatchHighlight
{
    private static readonly ConditionalWeakTable<ComboBoxAdv, Highlight> Highlights = [];

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(ComboBoxAdv), UIElement.KeyUpEvent, new KeyEventHandler(OnKeyUp), handledEventsToo: true);
        EventManager.RegisterClassHandler(typeof(ComboBoxAdv), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnPreviewKeyDown));
        EventManager.RegisterClassHandler(typeof(ComboBoxItemAdv), UIElement.MouseEnterEvent, new MouseEventHandler(OnItemMouseEnter));
    }

    /// <summary>A picker's highlighted match, and what it was worked out for.</summary>
    private sealed class Highlight
    {
        /// <summary>The text the match was worked out for; the highlight follows a change to it.</summary>
        public string Text { get; set; } = string.Empty;

        public object? Item { get; set; }

        public ComboBoxItemAdv? Container { get; set; }

        /// <summary>Where the mouse was over the picker when the match was highlighted.</summary>
        public Point MousePosition { get; set; }

        /// <summary>
        /// An arrow key moved on from the match here, so no key-up may reach the picker until a key goes down again: not
        /// the arrow's, nor a Ctrl or Shift held with it, nor the N or P of Vim's Ctrl+N and Ctrl+P, which press it.
        /// </summary>
        public bool SwallowKeyUps { get; set; }
    }

    private static void OnKeyUp(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBoxAdv picker)
        {
            return;
        }

        // The picker would refilter its list by the name the arrow key wrote into its box, leaving only that name. Any
        // key-up does it, until a key going down through the picker's own handling brings its filter text up to date.
        if (Highlights.TryGetValue(picker, out var highlight) && highlight.SwallowKeyUps)
        {
            e.Handled = true;
            return;
        }

        if (IsSearchable(picker) && TypingBox(picker) is { } box && ReferenceEquals(e.OriginalSource, box))
        {
            ScheduleHighlight(picker, Highlights.GetValue(picker, _ => new Highlight()));
        }
    }

    /// <summary>
    /// Highlights the match for the text typed, once the picker has refiltered its list (on this same key-up, after
    /// this handler) and laid out its rows.
    /// </summary>
    private static void ScheduleHighlight(ComboBoxAdv picker, Highlight highlight)
    {
        picker.Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            var text = TypingBox(picker)?.Text ?? string.Empty;
            if (text == highlight.Text && (highlight.Container?.IsHighlighted ?? highlight.Item is null))
            {
                return;
            }

            Clear(highlight);
            highlight.Text = text;
            if (text.Length == 0 || !picker.IsDropDownOpen || Match(picker, text) is not { } item
                || picker.ItemContainerGenerator.ContainerFromItem(item) is not ComboBoxItemAdv container)
            {
                return;
            }

            container.IsHighlighted = true;
            container.BringIntoView();
            highlight.Item = item;
            highlight.Container = container;
            highlight.MousePosition = Mouse.GetPosition(picker);
        });
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ComboBoxAdv picker || !Highlights.TryGetValue(picker, out var highlight))
        {
            return;
        }

        // A key going down reaches the picker's own handling, which brings its filter text up to date.
        highlight.SwallowKeyUps = false;
        if (highlight is not { Item: { } item, Container: { IsHighlighted: true } container }
            || !picker.IsDropDownOpen
            || TypingBox(picker) is not { } box
            || !ReferenceEquals(e.OriginalSource, box))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Enter or Key.Tab:
                // The picker's own Enter and Tab come next: with this pick made they close the list and keep it.
                Clear(highlight);
                if (!Equals(picker.SelectedItem, item))
                {
                    box.Text = DisplayName(picker, item);
                    picker.SelectedItem = item;
                }

                break;

            case Key.Down or Key.Up:
                // From the typed text the picker's own arrow keys start at the top of the list, or at its pick when the
                // text names one, which is then the match itself.
                if (picker.SelectedItem is not null)
                {
                    return;
                }

                var index = picker.ItemContainerGenerator.IndexFromContainer(container) + (e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                if (index < 0 || index >= picker.Items.Count
                    || picker.ItemContainerGenerator.ContainerFromIndex(index) is not ComboBoxItemAdv next)
                {
                    return;
                }

                // What the picker's own arrow key does to a row: into its box goes the name, and the row takes the focus.
                Clear(highlight);
                highlight.SwallowKeyUps = true;
                box.Text = DisplayName(picker, picker.Items[index]);
                next.Focus();
                next.IsHighlighted = true;
                break;
        }
    }

    private static void OnItemMouseEnter(object sender, MouseEventArgs e)
    {
        // A list refiltered under a still mouse raises MouseEnter too; only the mouse moving takes the highlight off.
        if (sender is ComboBoxItemAdv item
            && ItemsControl.ItemsControlFromItemContainer(item) is ComboBoxAdv picker
            && Highlights.TryGetValue(picker, out var highlight)
            && highlight.Container is { } container
            && !ReferenceEquals(container, item)
            && e.GetPosition(picker) != highlight.MousePosition)
        {
            Clear(highlight);
        }
    }

    private static void Clear(Highlight highlight)
    {
        if (highlight.Container is { } container && !container.IsKeyboardFocusWithin)
        {
            container.IsHighlighted = false;
        }

        highlight.Item = null;
        highlight.Container = null;
    }

    /// <summary>The name typed in full, or else the first starting with the text, or else the first containing it.</summary>
    private static object? Match(ComboBoxAdv picker, string text)
    {
        object? startsWith = null;
        object? contains = null;
        foreach (var item in picker.Items)
        {
            var name = DisplayName(picker, item);
            if (name.Equals(text, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }

            if (startsWith is null && name.StartsWith(text, StringComparison.OrdinalIgnoreCase))
            {
                startsWith = item;
            }
            else if (contains is null && name.Contains(text, StringComparison.OrdinalIgnoreCase))
            {
                contains = item;
            }
        }

        return startsWith ?? contains;
    }

    /// <summary>An item's name as the picker shows it and filters by it: its DisplayMemberPath property, or itself.</summary>
    private static string DisplayName(ComboBoxAdv picker, object? item)
    {
        var value = string.IsNullOrEmpty(picker.DisplayMemberPath) ? item : item?.GetType().GetProperty(picker.DisplayMemberPath)?.GetValue(item);
        return value?.ToString() ?? string.Empty;
    }

    /// <summary>A single pick, typed into and filtered as typed: what ComboBoxAdv's Suggest mode filters.</summary>
    private static bool IsSearchable(ComboBoxAdv picker) =>
        picker is { IsEditable: true, AllowMultiSelect: false, AutoCompleteMode: AutoCompleteModes.Suggest, ItemsSource: not null };

    /// <summary>The text box a single-pick editable picker is typed into.</summary>
    private static TextBox? TypingBox(ComboBoxAdv picker) => picker.Template?.FindName("PART_Editable", picker) as TextBox;
}
