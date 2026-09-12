using System.Windows;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI;

/// <summary>
/// Wires a ViewModelBase's ConfirmDelete interaction to a native MessageBox — one call per
/// View's constructor instead of repeating the same handler in every screen. Kept in the
/// View layer (not on ViewModelBase itself) since showing a MessageBox is what a View does
/// to fulfill the interaction; a future screen could register a different handler (a themed
/// dialog, or an auto-confirming stub in a test) without ViewModelBase needing to change.
/// </summary>
public static class ConfirmDeleteHandlerRegistration
{
    public static void RegisterNativeMessageBoxHandler(this ViewModelBase viewModel)
    {
        viewModel.ConfirmDelete.RegisterHandler(interaction =>
        {
            var result = MessageBox.Show(interaction.Input, "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            interaction.SetOutput(result == MessageBoxResult.Yes);
        });
    }
}
