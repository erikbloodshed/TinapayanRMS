using System.Reactive.Disposables;
using System.Windows;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI;

/// <summary>
/// Wires a ViewModelBase's ConfirmDelete and ConfirmUnsavedChanges interactions to native MessageBoxes — one call per
/// View's constructor instead of repeating the same handlers in every screen. Kept in the
/// View layer (not on ViewModelBase itself) since showing a MessageBox is what a View does
/// to fulfill the interaction; a future screen could register a different handler (a themed
/// dialog, or an auto-confirming stub in a test) without ViewModelBase needing to change.
/// </summary>
public static class ConfirmDeleteHandlerRegistration
{
    /// <returns>
    /// The registration. A screen that lives as long as the app can ignore it; a view built afresh
    /// each time over a longer-lived ViewModel (the Settings dialog's tabs) disposes it on close, so
    /// handlers don't pile up.
    /// </returns>
    public static IDisposable RegisterNativeMessageBoxHandler(this ViewModelBase viewModel) =>
        new CompositeDisposable(
            viewModel.ConfirmDelete.RegisterHandler(interaction =>
            {
                // "Confirm" rather than "Confirm Delete": the same interaction asks about anything that can't be undone,
                // Products' Revert among them, and the message itself says which.
                var result = MessageBox.Show(interaction.Input, "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                interaction.SetOutput(result == MessageBoxResult.Yes);
            }),
            viewModel.ConfirmUnsavedChanges.RegisterHandler(interaction =>
            {
                var result = MessageBox.Show(interaction.Input, "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                interaction.SetOutput(result switch
                {
                    MessageBoxResult.Yes => UnsavedChangesAnswer.Save,
                    MessageBoxResult.No => UnsavedChangesAnswer.DontSave,
                    _ => UnsavedChangesAnswer.Cancel,
                });
            }));
}
