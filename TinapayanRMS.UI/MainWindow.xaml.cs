using System.Windows;

namespace TinapayanRMS.UI;

public partial class MainWindow : Window
{
    public MainWindow(
        MainWindowViewModel viewModel,
        IngredientsViewModel ingredientsViewModel,
        PackagingMaterialsViewModel packagingMaterialsViewModel,
        SubAssembliesViewModel subAssembliesViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        IngredientsTab.Content = new IngredientsView(ingredientsViewModel);
        PackagingMaterialsTab.Content = new PackagingMaterialsView(packagingMaterialsViewModel);
        SubAssembliesTab.Content = new SubAssembliesView(subAssembliesViewModel);
    }
}
