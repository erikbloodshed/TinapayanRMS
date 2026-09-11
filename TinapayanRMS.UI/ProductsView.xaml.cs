using System.Windows.Controls;

namespace TinapayanRMS.UI;

public partial class ProductsView : UserControl
{
    public ProductsView(ProductsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
