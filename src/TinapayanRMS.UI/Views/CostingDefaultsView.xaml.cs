using System.Windows.Controls;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>The Settings dialog's Costing Defaults tab: where a new product goes, what its costing starts with, and a new raw material's cost buffer.</summary>
public sealed partial class CostingDefaultsView : UserControl
{
    public CostingDefaultsView(CostingDefaultsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
