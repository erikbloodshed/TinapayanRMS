using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using AwesomeAssertions;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// PickerSelectOnEntry: a picker's box selects its whole text as the keyboard enters it from outside the picker, by a
/// click or by Tab, and not as it comes back to the box from the picker's own list.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class PickerSelectOnEntryTests
{
    private static readonly string[] Categories = ["Bread", "Cake", "Pastry"];

    [Fact]
    public void A_click_into_a_picker_gives_its_box_the_keyboard_with_the_text_selected()
    {
        RunWithPicker((before, picker, box) =>
        {
            box.Text = "Pas";
            OffscreenGrid.Pump();

            Click(box);
            OffscreenGrid.Pump();

            Keyboard.FocusedElement.Should().BeSameAs(box);
            box.SelectedText.Should().Be("Pas");
        });
    }

    [Fact]
    public void Tabbing_into_a_picker_selects_its_text()
    {
        RunWithPicker((before, picker, box) =>
        {
            box.Text = "Pas";
            OffscreenGrid.Pump();

            before.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            OffscreenGrid.Pump();

            Keyboard.FocusedElement.Should().BeSameAs(box);
            box.SelectedText.Should().Be("Pas");
        });
    }

    [Fact]
    public void Typing_over_a_picked_name_selected_on_entry_keeps_the_letter_typed()
    {
        RunWithPicker((before, picker, box) =>
        {
            picker.SelectedItem = "Pastry";
            OffscreenGrid.Pump();
            before.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            OffscreenGrid.Pump();
            box.SelectedText.Should().Be("Pastry");

            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, box, "c"));
            OffscreenGrid.Pump();

            box.Text.Should().Be("c");
            box.CaretIndex.Should().Be(1);
            picker.SelectedItem.Should().BeNull();
        });
    }

    [Fact]
    public void A_letter_added_to_a_picked_name_stays()
    {
        RunWithPicker((before, picker, box) =>
        {
            picker.SelectedItem = "Cake";
            box.Focus();
            OffscreenGrid.Pump();
            box.CaretIndex = box.Text.Length;

            TextCompositionManager.StartComposition(new TextComposition(InputManager.Current, box, "s"));
            OffscreenGrid.Pump();

            box.Text.Should().Be("Cakes");
            picker.SelectedItem.Should().BeNull();
        });
    }

    [Fact]
    public void The_keyboard_coming_back_from_the_pickers_list_leaves_the_text_unselected()
    {
        RunWithPicker((before, picker, box) =>
        {
            box.Focus();
            box.Text = "Pas";
            picker.IsDropDownOpen = true;
            OffscreenGrid.Pump();
            box.CaretIndex = box.Text.Length;

            // "Pas" leaves Pastry alone in the list: the name an arrow key moves the keyboard onto, and typing brings it
            // back. Raised rather than focused, since a pop-up of an off-screen window doesn't reliably take the keyboard.
            var name = (ComboBoxItemAdv)picker.ItemContainerGenerator.ContainerFromIndex(0);
            box.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, name, box) { RoutedEvent = Keyboard.GotKeyboardFocusEvent });

            box.SelectionLength.Should().Be(0);
        });
    }

    /// <summary>
    /// A left click on the box, as the input manager raises it: the preview, then the bubbling press. Only the focus it
    /// gives: a raised click never reaches the box's own caret placement, which a real click's goes on to.
    /// </summary>
    private static void Click(TextBox box)
    {
        var press = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseDownEvent };
        box.RaiseEvent(press);
        press.RoutedEvent = UIElement.MouseDownEvent;
        box.RaiseEvent(press);
        var release = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left) { RoutedEvent = UIElement.PreviewMouseUpEvent };
        box.RaiseEvent(release);
        release.RoutedEvent = UIElement.MouseUpEvent;
        box.RaiseEvent(release);
    }

    /// <summary>A field, then a picker set up as the app's are, in an off-screen window, with the keyboard in the field.</summary>
    private static void RunWithPicker(Action<TextBox, ComboBoxAdv, TextBox> test) =>
        StaThread.RunLicensed(() =>
        {
            PickerSelectOnEntry.Register();
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

                test(before, picker, (TextBox)picker.Template.FindName("PART_Editable", picker));
            }
            finally
            {
                window.Close();
            }
        });
}
