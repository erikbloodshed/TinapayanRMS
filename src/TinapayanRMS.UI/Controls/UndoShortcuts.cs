using System.Windows;
using System.Windows.Input;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Ctrl+Z and Ctrl+Y for the screen the cursor is on, wherever it is on it.
///
/// It has to be a class handler on Window, and it has to be on the tunnelling PreviewKeyDown. Syncfusion's number
/// boxes (DoubleTextBox and its kin, which every quantity cell and every percentage field is) take Ctrl+Z and
/// Ctrl+Y for their own per-character undo and mark them handled in their PreviewKeyDown — even when that undo is
/// switched off. Tunnelling runs from the window down to whatever has the cursor, so a handler on the window sees
/// the key first; an InputBinding, which works on the way back up, would never be reached at all.
///
/// The screen is found by walking up from whatever has the cursor to the first DataContext that records undo
/// steps, so nothing has to be registered and no window needs to know which of its tabs is showing. A window whose
/// screen doesn't record steps — Settings, a prompt — is left alone, key and all.
/// </summary>
public static class UndoShortcuts
{
    /// <summary>Wires the shortcuts up for every window in the app. Called once, from App's static constructor.</summary>
    public static void Register() =>
        EventManager.RegisterClassHandler(
            typeof(Window), UIElement.PreviewKeyDownEvent, new KeyEventHandler(OnWindowPreviewKeyDown));

    private static void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.KeyboardDevice.Modifiers is not ModifierKeys.Control and not (ModifierKeys.Control | ModifierKeys.Shift))
        {
            return;
        }

        var shift = e.KeyboardDevice.Modifiers.HasFlag(ModifierKeys.Shift);
        var redo = e.Key is Key.Y || (e.Key is Key.Z && shift);
        if (e.Key is not Key.Z && e.Key is not Key.Y)
        {
            return;
        }

        // Ctrl+Shift+Y isn't a shortcut of ours; leave it to whatever else might want it.
        if (e.Key is Key.Y && shift)
        {
            return;
        }

        if (Undo(Keyboard.FocusedElement as DependencyObject, redo))
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Undoes (or redoes) one step on the screen <paramref name="focused"/> is on: Ctrl+Z and Ctrl+Y, and the Vim keys'
    /// <c>u</c> and Ctrl+R. False when it's on no screen that records steps.
    /// </summary>
    public static bool Undo(DependencyObject? focused, bool redo)
    {
        if (ScreenOf(focused) is not { } screen)
        {
            return false;
        }

        // A cell still being typed into is committed first, not thrown away. Everywhere else on these screens —
        // leaving a cell, leaving a grid, leaving a tab, Save — a half-typed value is kept, and an undo that
        // silently dropped it would be the one place that isn't so. Committed, it becomes the step this very
        // press then takes back, so Ctrl+Z always removes exactly one change the user can see.
        if (VisualTreeWalk.FindAncestor<SfDataGrid>(focused) is { } grid)
        {
            GridEditCommit.Commit(grid);
        }

        // Settles the step the commit above closed, and the one a field still holding the cursor has open —
        // nothing else would close that one, since focus never left it.
        screen.EndUndoStep();

        if (redo)
        {
            screen.TryRedo();
        }
        else
        {
            screen.TryUndo();
        }

        // Focus hasn't moved, so the field that has it would never reopen a step of its own for what's typed next.
        screen.BeginUndoStep();
        return true;
    }

    /// <summary>The screen the cursor is on, or null when it's on one that doesn't record undo steps.</summary>
    private static IUndoScreen? ScreenOf(DependencyObject? focused) =>
        VisualTreeWalk.SelfAndAncestors(focused)
            .OfType<FrameworkElement>()
            .Select(element => element.DataContext)
            .OfType<IUndoScreen>()
            .FirstOrDefault();
}
