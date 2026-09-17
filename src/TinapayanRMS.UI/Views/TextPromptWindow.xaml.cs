using System.Windows;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// A modal one-box prompt: a label, a text box and OK/Cancel. What the Raw Materials screen's Category
/// picker opens for its "&lt; Add New .. &gt;" row, so a category can be named without leaving the screen
/// for Settings. WPF has no input box of its own, and a MessageBox can't take text.
/// </summary>
public partial class TextPromptWindow : Window
{
    private TextPromptWindow(string title, string label, string initialValue)
    {
        InitializeComponent();
        Title = title;
        PromptLabel.Text = label;
        ValueTextBox.Text = initialValue;
    }

    /// <summary>
    /// Shows the prompt over <paramref name="owner"/>, starting with <paramref name="initialValue"/> selected so
    /// typing replaces it outright. Returns what was typed, or null if it was cancelled or left empty.
    /// </summary>
    public static string? Prompt(string title, string label, string initialValue, Window? owner)
    {
        var window = new TextPromptWindow(title, label, initialValue) { Owner = owner };
        window.ValueTextBox.Loaded += (_, _) =>
        {
            window.ValueTextBox.Focus();
            window.ValueTextBox.SelectAll();
        };

        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ValueTextBox.Text)
            ? window.ValueTextBox.Text
            : null;
    }

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    // ButtonAdv.IsCancel only wires the Escape key to a Click (per Syncfusion's own docs); unlike a native
    // Button.IsCancel, it doesn't also set DialogResult on its own, so both the Escape shortcut and a mouse
    // click on Cancel did nothing without this — the window just sat there.
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
