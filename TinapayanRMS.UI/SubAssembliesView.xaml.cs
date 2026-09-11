using System.Windows.Controls;

namespace TinapayanRMS.UI;

public partial class SubAssembliesView : UserControl
{
    public SubAssembliesView(SubAssembliesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
