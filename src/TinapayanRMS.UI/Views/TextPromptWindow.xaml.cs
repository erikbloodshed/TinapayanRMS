using System.Windows;
using System.Windows.Input;
using Syncfusion.SfSkinManager;
using TinapayanRMS.UI.ViewModels;


namespace TinapayanRMS.UI.Views;

/// <summary>
/// A modal one-box prompt: an optional line saying what it's for, a label, a text box and OK/Cancel. What the Raw
/// Materials and Products Category pickers open when a name typed into them isn't a category yet, so it can be added
/// without leaving the screen for Settings. WPF has no input box of its own, and a MessageBox can't take text.
/// <see cref="PromptWithChoice"/> adds a picker under the box: Move to In-House's name and Made category.
/// Chromeless: the card's own heading carries the title, and dragging it moves the dialog.
/// </summary>
public partial class TextPromptWindow : Window
{
    private TextPromptWindow(string title, string label, string initialValue, string? message)
    {
        InitializeComponent();

        // Title too, though nothing draws it: it's still the window's name to UI Automation and screen readers.
        Title = title;
        HeadingText.Text = title;
        PromptLabel.Text = label;
        ValueTextBox.Text = initialValue;
        MessageText.Text = message;
        MessageText.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Shows the prompt over <paramref name="owner"/>, starting with <paramref name="initialValue"/> selected so
    /// typing replaces it outright, under <paramref name="message"/> when there is one. Returns what was typed, or null
    /// if it was cancelled or left empty.
    /// </summary>
    public static string? Prompt(string title, string label, string initialValue, Window? owner, string? message = null)
    {
        var window = Create(title, label, initialValue, owner, message);
        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ValueTextBox.Text)
            ? window.ValueTextBox.Text
            : null;
    }

    /// <summary>
    /// <see cref="Prompt"/> with a picker of <see cref="ChoicePromptRequest.Choices"/> under the box, starting on
    /// <see cref="ChoicePromptRequest.InitialChoice"/>. Returns what was typed and the choice picked, or null if it was
    /// cancelled or the box was left empty.
    /// </summary>
    public static ChoicePromptResult? PromptWithChoice(ChoicePromptRequest request, Window? owner)
    {
        var window = Create(request.Title, request.Label, request.InitialValue, owner, request.Message);
        window.ValueRow.Margin = window.ValueRow.Margin with { Bottom = 12 };
        window.ChoiceRow.Visibility = Visibility.Visible;
        window.ChoiceLabel.Text = request.ChoiceLabel;
        window.ChoiceComboBox.ItemsSource = request.Choices;
        window.ChoiceComboBox.SelectedIndex = request.InitialChoice;

        return window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ValueTextBox.Text) && window.ChoiceComboBox.SelectedIndex >= 0
            ? new ChoicePromptResult(window.ValueTextBox.Text, window.ChoiceComboBox.SelectedIndex)
            : null;
    }

    private static TextPromptWindow Create(string title, string label, string initialValue, Window? owner, string? message)
    {
        var window = new TextPromptWindow(title, label, initialValue, message) { Owner = owner };

        // SfSkinManager themes only the windows it's given: a new Window isn't in its owner's tree, Owner or not, so
        // without this its buttons and box fell back to Syncfusion's unthemed default look (as MainWindow does for Settings).
        if (owner is not null)
        {
            SfSkinManager.SetTheme(window, SfSkinManager.GetTheme(owner));
        }

        window.ValueTextBox.Loaded += (_, _) =>
        {
            window.ValueTextBox.Focus();
            window.ValueTextBox.SelectAll();
        };
        return window;
    }

    private void Heading_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => DragMove();

    private void OkButton_Click(object sender, RoutedEventArgs e) => DialogResult = true;

    // ButtonAdv.IsCancel only wires the Escape key to a Click (per Syncfusion's own docs); unlike a native
    // Button.IsCancel, it doesn't also set DialogResult on its own, so both the Escape shortcut and a mouse
    // click on Cancel did nothing without this — the window just sat there.
    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
