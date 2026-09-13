using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using ReactiveUI;
using Syncfusion.UI.Xaml.NavigationDrawer;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Views;

public partial class MainWindow : Window
{
    // Each tab's screen LoadCommand, run whenever that tab is shown (see LoadSelectedTab).
    private readonly Dictionary<TabItemExt, ReactiveCommand<RxVoid, RxVoid>> _loadCommands = [];
    private readonly HashSet<TabItemExt> _loadingTabs = [];

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

        _loadCommands[IngredientsTab] = ingredientsViewModel.LoadCommand;
        _loadCommands[PackagingMaterialsTab] = packagingMaterialsViewModel.LoadCommand;
        _loadCommands[SubAssembliesTab] = subAssembliesViewModel.LoadCommand;
        _loadCommands[ProductsTab] = productsViewModel.LoadCommand;
        _loadCommands[ReportsTab] = reportsViewModel.LoadCommand;
        _loadCommands[UnitsOfMeasureTab] = unitsOfMeasureViewModel.LoadCommand;

        // The tab that's selected at startup; every later switch goes through SelectionChanged.
        Loaded += (_, _) => LoadSelectedTab();
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

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles, so a grid or picker inside a tab raises it here too.
        if (ReferenceEquals(e.OriginalSource, MainTabControl) && IsLoaded)
        {
            LoadSelectedTab();
        }
    }

    /// <summary>
    /// Refreshes the shown tab's data, picking up whatever other tabs changed. The screen keeps its
    /// state because every LoadCommand merges rather than reloads (PLAN.md). A refresh still running
    /// from a quick earlier visit is left to finish rather than started twice.
    /// </summary>
    private void LoadSelectedTab()
    {
        if (MainTabControl.SelectedItem is not TabItemExt tab
            || !_loadCommands.TryGetValue(tab, out var load)
            || !_loadingTabs.Add(tab))
        {
            return;
        }

        load.Execute().Finally(() => _loadingTabs.Remove(tab)).Subscribe();
    }
}
