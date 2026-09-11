using System.Windows.Controls;

namespace TinapayanRMS.UI;

public partial class PackagingMaterialsView : UserControl
{
    public PackagingMaterialsView(PackagingMaterialsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
