using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Syncfusion.Windows.Tools.Controls;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// A picker that's typed into (ComboBoxAdv, editable, as App makes every one) selects its whole text as the keyboard
/// enters it, by a click or by Tab, so what's typed next replaces it.
/// <list type="bullet">
/// <item>A click selected the text only for a moment: the picker selects it as its box takes the keyboard
/// (Part_IsEdit_GotFocus, and only with a name picked), then the box's own mouse handling put the caret where the click
/// landed. The first click into the box now only gives it the keyboard; a click into a box that already has it places
/// the caret as usual, and a double-click still selects a word.</item>
/// <item>The text is selected whether or not it names a pick, so typed text that matched nothing is selected too.</item>
/// <item>Only on the way in from outside the picker. The keyboard also comes back to the box from the picker's own list
/// (an arrow key moves it onto a name, and typing brings it back), and selecting then would have the next letter typed
/// replace the text instead of adding to it.</item>
/// <item>A letter typed over a picked name stays. The picker dropped it: text that no longer names its pick clears the
/// pick (Part_IsEdit_TextChanged), and clearing the pick empties the box (OnSelectionChanged) unless the key was
/// Backspace or Delete, so the first letter typed left the box empty. The pick is now cleared first, then the letter
/// typed in, as the box would have taken it.</item>
/// </list>
/// Class handlers, as PickerMatchHighlight's are, since some pickers never raise Loaded. Registered once for the whole
/// app, from App's static constructor.
/// </summary>
public static class PickerSelectOnEntry
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewMouseLeftButtonDownEvent, new MouseButtonEventHandler(OnPreviewMouseLeftButtonDown));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.GotKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnGotKeyboardFocus));
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewTextInputEvent, new TextCompositionEventHandler(OnPreviewTextInput));
    }

    private static void OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (TypingBoxOf(sender) is not (var box, var picker) || box.IsKeyboardFocusWithin || IsWithin(picker, Keyboard.FocusedElement as DependencyObject))
        {
            return;
        }

        // Taking the keyboard selects the text (OnGotKeyboardFocus); the box's own handling would put the caret down.
        if (box.Focus())
        {
            e.Handled = true;
        }
    }

    private static void OnGotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (TypingBoxOf(sender) is (var box, var picker) && ReferenceEquals(e.NewFocus, box) && !IsWithin(picker, e.OldFocus as DependencyObject))
        {
            box.SelectAll();
        }
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (TypingBoxOf(sender) is not (var box, var picker) || picker.SelectedItem is null || box.IsReadOnly
            || string.IsNullOrEmpty(e.Text) || e.Text.Any(char.IsControl))
        {
            return;
        }

        var start = box.SelectionStart;
        var text = box.Text.Remove(start, box.SelectionLength).Insert(start, e.Text);
        picker.SelectedItem = null;
        box.Text = text;
        box.CaretIndex = start + e.Text.Length;
        e.Handled = true;
    }

    /// <summary>The box an editable picker is typed into, and the picker, when <paramref name="sender"/> is one.</summary>
    private static (TextBox Box, ComboBoxAdv Picker)? TypingBoxOf(object sender) =>
        sender is TextBox { TemplatedParent: ComboBoxAdv { IsEditable: true } picker } box
        && ReferenceEquals(picker.Template?.FindName("PART_Editable", picker), box)
            ? (box, picker)
            : null;

    /// <summary>
    /// Whether <paramref name="element"/> is part of the picker, its open list included: not the picker itself, which
    /// hands the keyboard on to its box as it takes it from outside. Up through each element's logical parent where it
    /// has one, since the list's pop-up is no visual child of the picker.
    /// </summary>
    private static bool IsWithin(ComboBoxAdv picker, DependencyObject? element)
    {
        if (ReferenceEquals(element, picker))
        {
            return false;
        }

        for (var current = element; current is not null; current = (current as FrameworkElement)?.Parent ?? ParentOf(current))
        {
            if (ReferenceEquals(current, picker))
            {
                return true;
            }
        }

        return false;
    }

    private static DependencyObject? ParentOf(DependencyObject element) =>
        element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
}
