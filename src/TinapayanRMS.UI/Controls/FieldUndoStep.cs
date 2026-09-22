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

    /// <summary>
    /// Set alongside <see cref="RecorderProperty"/> on a box whose value only reaches the form when focus leaves
    /// it (a binding with <c>UpdateSourceTrigger=LostFocus</c>). Until then the screen can't see what's been
    /// typed, so while the box has the cursor it keeps Save offered rather than deciding there's nothing to save.
    /// </summary>
    public static readonly DependencyProperty LandsOnLeaveProperty = DependencyProperty.RegisterAttached(
        "LandsOnLeave", typeof(bool), typeof(FieldUndoStep), new PropertyMetadata(false));

    public static bool GetLandsOnLeave(DependencyObject element) => (bool)element.GetValue(LandsOnLeaveProperty);

    public static void SetLandsOnLeave(DependencyObject element, bool value) => element.SetValue(LandsOnLeaveProperty, value);

    private static IUndoRecorder? RecorderOf(object sender) =>
        sender is DependencyObject element ? GetRecorder(element) : null;

    private static bool LandsOnLeave(object sender) => sender is DependencyObject element && GetLandsOnLeave(element);

    private static void OnGotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (RecorderOf(sender) is { } recorder)
        {
            recorder.BeginUndoStep();
            if (LandsOnLeave(sender))
            {
                recorder.IsEditPending = true;
            }
        }
    }

    private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (RecorderOf(sender) is { } recorder)
        {
            if (LandsOnLeave(sender))
            {
                recorder.IsEditPending = false;
            }

            recorder.EndUndoStep();
        }
    }
}
