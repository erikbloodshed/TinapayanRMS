using System.Windows.Controls;

namespace TinapayanRMS.UI;

public partial class ReportsView : UserControl
{
    public ReportsView(ReportsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
