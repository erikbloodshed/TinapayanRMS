using System.Windows.Controls;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>The Settings dialog's Costing Defaults tab: what a new product's costing starts with.</summary>
public partial class CostingDefaultsView : UserControl
{
    public CostingDefaultsView(CostingDefaultsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
