using System.Windows.Controls;

namespace TinapayanRMS.UI;

public partial class ProductsView : UserControl
{
    public ProductsView(ProductsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
