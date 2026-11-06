using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>
/// A link hint on a picker (VimKeys.FocusPicker) puts the keyboard in the box it's typed in, after a field before it,
/// as the app's pickers are: editable, with suggestions (App's ApplyPickerDefaults).
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class VimKeysPickerTests
{
    private static readonly string[] Categories = ["Bread", "Cake", "Pastry"];

    [Test]
    public void A_hint_puts_the_keyboard_in_the_pickers_box()
    {
        RunWithPicker((before, picker) =>
        {
            VimKeys.FocusPicker(picker);
            OffscreenGrid.Pump();

            Keyboard.FocusedElement.Should().BeSameAs(TypingBox(picker));
        });
    }

    [Test]
    public void A_hint_still_reaches_a_picker_left_by_Shift_Tab()
    {
        RunWithPicker((before, picker) =>
        {
            // What a Shift+Tab out of its box does to a picker (ComboBoxAdv's Part_IsEdit_PreviewKeyDown), for good.
            TypingBox(picker).Focus();
            picker.Focusable = false;
            before.Focus();
            OffscreenGrid.Pump();

            VimKeys.FocusPicker(picker);
            OffscreenGrid.Pump();

            Keyboard.FocusedElement.Should().BeSameAs(TypingBox(picker));
        });
    }

    private static TextBox TypingBox(ComboBoxAdv picker) => (TextBox)picker.Template.FindName("PART_Editable", picker);

    /// <summary>A field, then a picker, in an off-screen window, with the keyboard in the field.</summary>
    private static void RunWithPicker(Action<TextBox, ComboBoxAdv> test) =>
        StaThread.RunLicensed(() =>
        {
            var before = new TextBox();
            var picker = new ComboBoxAdv
            {
                IsEditable = true,
                AutoCompleteMode = AutoCompleteModes.Suggest,
                ItemsSource = Categories,
            };
            var window = new Window
            {
                Content = new StackPanel { Children = { before, picker } },
                Width = 400,
                Height = 300,
                Left = -20000,
                Top = -20000,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
            };
            try
            {
                window.Show();
                before.Focus();
                OffscreenGrid.Pump();

                test(before, picker);
            }
            finally
            {
                window.Close();
            }
        });
}
