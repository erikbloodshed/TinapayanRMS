using System.Windows;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// A modal one-box prompt: a label, a text box and OK/Cancel. What the Raw Materials screen's Category
/// picker opens for its "&lt; Add New .. &gt;" row, so a category can be named without leaving the screen
/// for Settings. WPF has no input box of its own, and a MessageBox can't take text.
/// </summary>
public partial class TextPromptWindow : Window
{
    private TextPromptWindow(string title, string label)
    {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = label;
    }

    /// <summary>Shows the prompt over <paramref name="owner"/>. Returns what was typed, or null if it was cancelled or left empty.</summary>
    public static string? Prompt(string title, string label, Window? owner)
    {
        var window = new TextPromptWindow(title, label) { Owner = owner };
        window.ValueTextBox.Loaded += (_, _) => window.ValueTextBox.Focus();

        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ValueTextBox.Text)
            ? window.ValueTextBox.Text
            : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
