using System.Windows.Controls;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

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
