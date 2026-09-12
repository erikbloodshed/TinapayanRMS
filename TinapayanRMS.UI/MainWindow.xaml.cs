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

    // Closing a tab removes it from MainTabControl.Items, so re-add it here if needed before selecting it.
    private void ShowTab(TabItemExt tab)
    {
        if (!MainTabControl.Items.Contains(tab))
        {
            MainTabControl.Items.Add(tab);
        }

        MainTabControl.SelectedItem = tab;
        NavigationDrawer.IsOpen = false;
    }
}
