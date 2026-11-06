using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using AwesomeAssertions;
using TinapayanRMS.UI.Tests.Fixtures;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Tests.Vim;

/// <summary>
/// With the Vim keys on, the keyboard can land on text content, not only on controls: a Hyperlink takes it when it's
/// clicked (Hyperlink.OnMouseLeftButtonDown focuses it). It isn't a Visual, so working out where the keyboard is
/// (VimKeys.PlaceOf, through VisualTreeWalk) has to step out of it by its logical parent.
/// </summary>
[NotInParallel(OffscreenGridCollection.Name)]
public class VimKeysTextContentTests
{
    [Test]
    public void A_hyperlink_can_take_the_keyboard_with_the_keys_on()
    {
        RunWithLink((field, link) =>
        {
            link.Focus();
            OffscreenGrid.Pump();

            Keyboard.FocusedElement.Should().BeSameAs(link);
        });
    }

    [Test]
    public void The_keys_go_on_working_after_a_hyperlink_had_the_keyboard()
    {
        RunWithLink((field, link) =>
        {
            link.Focus();
            OffscreenGrid.Pump();

            field.Focus();
            OffscreenGrid.Pump();

            Keyboard.FocusedElement.Should().BeSameAs(field);
            VimKeys.State.Mode.Should().Be(VimMode.Insert, "a field reached by a click or the app is for typing");
        });
    }

    /// <summary>A field, then a line of text with a link in it, in an off-screen window, with the Vim keys on.</summary>
    private static void RunWithLink(Action<TextBox, Hyperlink> test) =>
        StaThread.RunLicensed(() =>
        {
            var field = new TextBox();
            var link = new Hyperlink(new Run("Open the manual"));
            var window = new Window
            {
                Content = new StackPanel { Children = { field, new TextBlock(link) } },
                Width = 400,
                Height = 300,
                Left = -20000,
                Top = -20000,
                WindowStartupLocation = WindowStartupLocation.Manual,
                ShowInTaskbar = false,
            };
            VimKeys.Register();
            try
            {
                window.Show();
                OffscreenGrid.Pump();
                VimKeys.State.IsEnabled = true;

                test(field, link);
            }
            finally
            {
                VimKeys.State.IsEnabled = false;
                window.Close();
            }
        });
}
