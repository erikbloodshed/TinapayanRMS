using System.Windows;
using Syncfusion.UI.Xaml.NavigationDrawer;
using Syncfusion.Windows.Tools.Controls;

namespace TinapayanRMS.UI;

public partial class MainWindow : Window
{
    public MainWindow(
        MainWindowViewModel viewModel,
        IngredientsViewModel ingredientsViewModel,
        PackagingMaterialsViewModel packagingMaterialsViewModel,
        SubAssembliesViewModel subAssembliesViewModel,
        ProductsViewModel productsViewModel,
        ReportsViewModel reportsViewModel,
        UnitsOfMeasureViewModel unitsOfMeasureViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        IngredientsTab.Content = new IngredientsView(ingredientsViewModel);
        PackagingMaterialsTab.Content = new PackagingMaterialsView(packagingMaterialsViewModel);
        SubAssembliesTab.Content = new SubAssembliesView(subAssembliesViewModel);
        ProductsTab.Content = new ProductsView(productsViewModel);
        ReportsTab.Content = new ReportsView(reportsViewModel);
        UnitsOfMeasureTab.Content = new UnitsOfMeasureView(unitsOfMeasureViewModel);

        IngredientsNavItem.Tag = IngredientsTab;
        PackagingMaterialsNavItem.Tag = PackagingMaterialsTab;
        SubAssembliesNavItem.Tag = SubAssembliesTab;
        ProductsNavItem.Tag = ProductsTab;
        ReportsNavItem.Tag = ReportsTab;
        UnitsOfMeasureNavItem.Tag = UnitsOfMeasureTab;
    }

    private void NavigationDrawer_ItemClicked(object sender, NavigationItemClickedEventArgs e)
    {
        if (e.Item.Tag is TabItemExt tab)
        {
            ShowTab(tab);
        }
    }

    // MainTabControl.CloseMode="Hide" (set in XAML) means closing a tab only collapses its
    // TabItemExt rather than removing it from Items, so it's always still there to select —
    // just possibly Visibility.Collapsed from having been closed.
    private void ShowTab(TabItemExt tab)
    {
        tab.Visibility = Visibility.Visible;
        MainTabControl.SelectedItem = tab;
        NavigationDrawer.IsOpen = false;
    }
}
