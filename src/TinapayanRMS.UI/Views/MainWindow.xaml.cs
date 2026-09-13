using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using ReactiveUI;
using Syncfusion.SfSkinManager;
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
    private readonly UnitsOfMeasureViewModel _unitsOfMeasureViewModel;
    private readonly CategoriesViewModel _categoriesViewModel;

    public MainWindow(
        MainWindowViewModel viewModel,
        RawMaterialsViewModel rawMaterialsViewModel,
        SubAssembliesViewModel subAssembliesViewModel,
        ProductsViewModel productsViewModel,
        ReportsViewModel reportsViewModel,
        UnitsOfMeasureViewModel unitsOfMeasureViewModel,
        CategoriesViewModel categoriesViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _unitsOfMeasureViewModel = unitsOfMeasureViewModel;
        _categoriesViewModel = categoriesViewModel;

        RawMaterialsTab.Content = new RawMaterialsView(rawMaterialsViewModel);
        SubAssembliesTab.Content = new SubAssembliesView(subAssembliesViewModel);
        ProductsTab.Content = new ProductsView(productsViewModel);
        ReportsTab.Content = new ReportsView(reportsViewModel);

        RawMaterialsNavItem.Tag = RawMaterialsTab;
        SubAssembliesNavItem.Tag = SubAssembliesTab;
        ProductsNavItem.Tag = ProductsTab;
        ReportsNavItem.Tag = ReportsTab;

        _loadCommands[RawMaterialsTab] = rawMaterialsViewModel.LoadCommand;
        _loadCommands[SubAssembliesTab] = subAssembliesViewModel.LoadCommand;
        _loadCommands[ProductsTab] = productsViewModel.LoadCommand;
        _loadCommands[ReportsTab] = reportsViewModel.LoadCommand;

        // The tab that's selected at startup; every later switch goes through SelectionChanged.
        Loaded += (_, _) => LoadSelectedTab();
    }

    private void NavigationDrawer_ItemClicked(object sender, NavigationItemClickedEventArgs e)
    {
        if (ReferenceEquals(e.Item, SettingsNavItem))
        {
            OpenSettings();
        }
        else if (e.Item.Tag is TabItemExt tab)
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

    /// <summary>
    /// Units of measure and categories, edited in a dialog over the current screen. Once it closes,
    /// the screen on show refreshes so its pickers include anything added or renamed; the other tabs
    /// pick the changes up when they're next shown, as they do for any change.
    /// </summary>
    private void OpenSettings()
    {
        NavigationDrawer.IsOpen = false;

        var settings = new SettingsWindow(_unitsOfMeasureViewModel, _categoriesViewModel) { Owner = this };
        SfSkinManager.SetTheme(settings, SfSkinManager.GetTheme(this));
        settings.ShowDialog();

        LoadSelectedTab();
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
