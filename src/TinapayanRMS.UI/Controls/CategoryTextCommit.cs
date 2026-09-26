using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Settles what's typed into an editable Category picker when it's left or Enter is pressed in it, so a name that isn't
/// a category yet brings up a prompt to add it (ICategoryPickerScreen). Set
/// <c>controls:CategoryTextCommit.Screen="{Binding}"</c> on the picker.
///
/// Focus within rather than the picker's own focus events: the picker's text box and its list are elements of their
/// own, and moving between them isn't leaving the picker. The commit waits for the dispatcher to go idle, so the
/// picker has finished with the key or click first: Enter on a suggestion, or a click in the list, picks that
/// category before the screen reads what's typed.
/// </summary>
public static class CategoryTextCommit
{
    public static readonly DependencyProperty ScreenProperty = DependencyProperty.RegisterAttached(
        "Screen", typeof(ICategoryPickerScreen), typeof(CategoryTextCommit), new PropertyMetadata(null, OnScreenChanged));

    public static ICategoryPickerScreen? GetScreen(DependencyObject element) => (ICategoryPickerScreen?)element.GetValue(ScreenProperty);

    public static void SetScreen(DependencyObject element, ICategoryPickerScreen? value) => element.SetValue(ScreenProperty, value);

    private static void OnScreenChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement picker)
        {
            return;
        }

        picker.IsKeyboardFocusWithinChanged -= OnFocusWithinChanged;
        picker.PreviewKeyDown -= OnPreviewKeyDown;

        if (e.NewValue is not ICategoryPickerScreen)
        {
            return;
        }

        picker.IsKeyboardFocusWithinChanged += OnFocusWithinChanged;
        picker.PreviewKeyDown += OnPreviewKeyDown;
    }

    private static void OnFocusWithinChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            GetScreen((DependencyObject)sender)?.BeginCategoryEdit();
        }
        else
        {
            Commit((UIElement)sender);
        }
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit((UIElement)sender);
        }
    }

    private static void Commit(UIElement picker) =>
        picker.Dispatcher.InvokeAsync(
            () => GetScreen(picker)?.CommitCategoryTextAsync() ?? Task.CompletedTask,
            DispatcherPriority.Background);
}
