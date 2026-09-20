using System.Windows;
using System.Windows.Input;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Marks out one undo step around a field, from the moment it takes the cursor to the moment it loses it, so a
/// name or a percentage typed a character at a time comes back in one press rather than one per keystroke. Set
/// <c>controls:FieldUndoStep.Recorder="{Binding}"</c> on a box or picker the form is typed into.
///
/// Focus rather than a timer, because "typed and left" is the rule the screen already works by elsewhere
/// (GridEditCommit commits a grid on the way out of it), and because a field left showing the value it started
/// with settles into no step at all.
/// </summary>
public static class FieldUndoStep
{
    public static readonly DependencyProperty RecorderProperty = DependencyProperty.RegisterAttached(
        "Recorder", typeof(IUndoRecorder), typeof(FieldUndoStep), new PropertyMetadata(null, OnRecorderChanged));

    public static IUndoRecorder? GetRecorder(DependencyObject element) => (IUndoRecorder?)element.GetValue(RecorderProperty);

    public static void SetRecorder(DependencyObject element, IUndoRecorder? value) => element.SetValue(RecorderProperty, value);

    private static void OnRecorderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not FrameworkElement field)
        {
            return;
        }

        field.GotKeyboardFocus -= OnGotFocus;
        field.LostKeyboardFocus -= OnLostFocus;

        if (e.NewValue is not IUndoRecorder)
        {
            return;
        }

        field.GotKeyboardFocus += OnGotFocus;
        field.LostKeyboardFocus += OnLostFocus;
    }

    private static IUndoRecorder? RecorderOf(object sender) =>
        sender is DependencyObject element ? GetRecorder(element) : null;

    private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e) => RecorderOf(sender)?.BeginUndoStep();

    private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e) => RecorderOf(sender)?.EndUndoStep();
}
