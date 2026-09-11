using System.Windows;

namespace TinapayanRMS.UI;

public partial class MainWindow : Window
{
    public MainWindow(
        MainWindowViewModel viewModel,
        IngredientsViewModel ingredientsViewModel,
        PackagingMaterialsViewModel packagingMaterialsViewModel,
        SubAssembliesViewModel subAssembliesViewModel,
        ProductsViewModel productsViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        IngredientsTab.Content = new IngredientsView(ingredientsViewModel);
        PackagingMaterialsTab.Content = new PackagingMaterialsView(packagingMaterialsViewModel);
        SubAssembliesTab.Content = new SubAssembliesView(subAssembliesViewModel);
        ProductsTab.Content = new ProductsView(productsViewModel);
    }
}
