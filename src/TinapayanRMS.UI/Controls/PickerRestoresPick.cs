using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using Syncfusion.Windows.Tools.Controls;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// A picker whose text was deleted shows its value again once the keyboard leaves it, when that value can't be empty
/// (2026-10-16, at the user's request: "When i delete a value inside the Raw Material Type combobox and switch between
/// raw materials or i close the raw material tab, the Type combobox stays empty").
/// <list type="bullet">
/// <item>Deleting a picker's text clears its pick (ComboBoxAdv's Part_IsEdit_PreviewKeyDown, on Backspace or Delete).
/// Bound to a value that can't be empty, such as Raw Materials' Type and Source (enums), the empty pick can't be written
/// back: the binding fails to convert it and the screen keeps its value, so the picker and the screen disagree. Nothing
/// put them right again: another material of the same type sets the value it already holds, which raises no change.</item>
/// <item>So when the keyboard leaves such a picker, empty and with its binding in error, the picker reads the screen's
/// value back. Not while it's typed in: Backspace over the whole name, then typing another, would have the old name
/// come back after the first key.</item>
/// <item>A picker whose value can be empty (a category, a base unit) took the empty pick, so its binding is in no error
/// and it stays empty.</item>
/// </list>
/// A class handler, as PickerMatchHighlight's are, since some pickers never raise Loaded. Registered once for the whole
/// app, from App's static constructor.
/// </summary>
public static class PickerRestoresPick
{
    private static readonly DependencyProperty[] PickProperties = [Selector.SelectedItemProperty, Selector.SelectedValueProperty];

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        EventManager.RegisterClassHandler(typeof(ComboBoxAdv), UIElement.LostKeyboardFocusEvent, new KeyboardFocusChangedEventHandler(OnLostKeyboardFocus), handledEventsToo: true);
    }

    private static void OnLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        // Raised as the keyboard leaves any part of the picker, its box and its list's rows too.
        if (sender is not ComboBoxAdv picker || picker.IsKeyboardFocusWithin)
        {
            return;
        }

        foreach (var property in PickProperties)
        {
            if (picker.GetValue(property) is null && BindingOperations.GetBindingExpression(picker, property) is { HasError: true } binding)
            {
                binding.UpdateTarget();
            }
        }
    }
}
