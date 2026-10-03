using System.Windows.Controls;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>The Settings dialog's Keyboard tab: the Vim keys, on or off.</summary>
public partial class KeyboardSettingsView : UserControl
{
    public KeyboardSettingsView(KeyboardSettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
