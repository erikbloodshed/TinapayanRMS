using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using AwesomeAssertions;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// PickerRestoresPick: a picker emptied while bound to a value that can't be empty shows that value again once the
/// keyboard leaves it, as Raw Materials' Type does when its text is deleted.
/// </summary>
[Collection(OffscreenGridCollection.Name)]
public class PickerRestoresPickTests
{
    private static readonly string[] Notes = ["Bread", "Cake"];

    public enum Kind
    {
        Ingredient,
        Packaging,
    }

    public sealed class Form : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public Kind Kind
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;
                    Raise();
                }
            }
        } = Kind.Packaging;

        public string? Note
        {
            get;
            set
            {
                if (field != value)
                {
                    field = value;
                    Raise();
                }
            }
        } = "Bread";

        private void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    [Fact]
    public void A_picker_emptied_shows_its_value_again_once_the_keyboard_leaves()
    {
        RunWithPicker(nameof(Form.Kind), Enum.GetValues<Kind>(), (before, picker, box, form) =>
        {
            box.Focus();
            picker.SelectedItem = null;
            OffscreenGrid.Pump();
            form.Kind.Should().Be(Kind.Packaging, "an empty pick can't be written to a value that can't be empty");

            before.Focus();
            OffscreenGrid.Pump();

            picker.SelectedItem.Should().Be(Kind.Packaging);
            box.Text.Should().Be("Packaging");
            Validation.GetHasError(picker).Should().BeFalse();
        });
    }

    [Fact]
    public void A_picker_emptied_and_left_follows_a_value_set_to_the_same_again()
    {
        RunWithPicker(nameof(Form.Kind), Enum.GetValues<Kind>(), (before, picker, box, form) =>
        {
            box.Focus();
            picker.SelectedItem = null;
            before.Focus();
            OffscreenGrid.Pump();

            // Another material of the same type: the value set doesn't change, so nothing would tell the picker.
            form.Kind = Kind.Packaging;
            OffscreenGrid.Pump();

            box.Text.Should().Be("Packaging");
        });
    }

    [Fact]
    public void A_picker_whose_value_can_be_empty_stays_empty()
    {
        RunWithPicker(nameof(Form.Note), Notes, (before, picker, box, form) =>
        {
            box.Focus();
            picker.SelectedItem = null;
            before.Focus();
            OffscreenGrid.Pump();

            form.Note.Should().BeNull();
            picker.SelectedItem.Should().BeNull();
            box.Text.Should().BeEmpty();
        });
    }

    /// <summary>A field, then a picker set up as the app's are and bound to the form, in an off-screen window.</summary>
    private static void RunWithPicker(string path, System.Collections.IEnumerable items, Action<TextBox, ComboBoxAdv, TextBox, Form> test) =>
        StaThread.RunLicensed(() =>
        {
            PickerRestoresPick.Register();
            var form = new Form();
            var before = new TextBox();
            var picker = new ComboBoxAdv
            {
                IsEditable = true,
                AutoCompleteMode = AutoCompleteModes.Suggest,
                ItemsSource = items,
                DataContext = form,
            };
            picker.SetBinding(Selector.SelectedItemProperty, new Binding(path) { Mode = BindingMode.TwoWay });
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

                test(before, picker, (TextBox)picker.Template.FindName("PART_Editable", picker), form);
            }
            finally
            {
                window.Close();
            }
        });
}
