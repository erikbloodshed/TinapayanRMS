using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ReactiveUI;
using Syncfusion.SfSkinManager;
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
    private readonly ProductCategoriesViewModel _productCategoriesViewModel;
    private readonly SubAssemblyRolesViewModel _rolesViewModel;
    private readonly CostingDefaultsViewModel _costingDefaultsViewModel;

    public MainWindow(
        MainWindowViewModel viewModel,
        RawMaterialsViewModel rawMaterialsViewModel,
        SubAssembliesViewModel subAssembliesViewModel,
        ProductsViewModel productsViewModel,
        ReportsViewModel reportsViewModel,
        UnitsOfMeasureViewModel unitsOfMeasureViewModel,
        CategoriesViewModel categoriesViewModel,
        ProductCategoriesViewModel productCategoriesViewModel,
        SubAssemblyRolesViewModel rolesViewModel,
        CostingDefaultsViewModel costingDefaultsViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _unitsOfMeasureViewModel = unitsOfMeasureViewModel;
        _categoriesViewModel = categoriesViewModel;
        _productCategoriesViewModel = productCategoriesViewModel;
        _rolesViewModel = rolesViewModel;
        _costingDefaultsViewModel = costingDefaultsViewModel;

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

        // A Used By tile on Sub-Assemblies opens its product on Products. Picked before the tab shows, so a list
        // not loaded yet has the product waiting for the load that showing the tab starts.
        subAssembliesViewModel.ProductOpenRequests.Subscribe(productId =>
        {
            productsViewModel.OpenProduct(productId);
            ShowTab(ProductsTab);
        });

        // The tab that's selected at startup; every later switch goes through SelectionChanged.
        Loaded += (_, _) => LoadSelectedTab();
    }

    /// <summary>
    /// Summons the menu, or puts it away. ToggleDrawer() rather than IsOpen = true: it's the control's
    /// documented way in (and what Syncfusion's own demos call), and it clears the secondary drawer's
    /// state on the way. The toggle sits in the header row, outside the drawer's scrim, so it stays
    /// clickable while the menu is open and this closes it again.
    /// </summary>
    private void ToggleNavigationButton_Click(object sender, RoutedEventArgs e)
    {
        NavigationDrawer.ToggleDrawer();

        if (NavigationDrawer.IsOpen)
        {
            // The panel is Collapsed until the open animation starts, and a collapsed element can't take
            // focus, so this waits for the layout pass. Focus goes to the list itself, not to a row: a
            // row would light up the moment the menu opened, which is the "current page" look that was
            // just taken out. The first Down then steps onto the top row (see MoveMenuFocus).
            Dispatcher.BeginInvoke(() => NavigationMenu.Focus(), DispatcherPriority.Input);
        }
    }

    /// <summary>
    /// Picking a destination navigates to it. Driven by the click, not by the ListBox's selection: the
    /// menu doesn't mark the page showing (the tab strip right below already does), so its SelectedItem
    /// would be both invisible and free to drift from the tab actually open — and a click on a row that
    /// happened to still be selected raises no SelectionChanged, so it would close the menu without
    /// navigating. The ListBox is kept for its templated rows and their hover, not for a selection.
    /// </summary>
    private void NavigationMenuItem_Clicked(object sender, MouseButtonEventArgs e) => Navigate(sender as ListBoxItem);

    private void Navigate(ListBoxItem? row)
    {
        if (row is { Tag: TabItemExt tab })
        {
            ShowTab(tab);   // closes the drawer
        }
        else
        {
            CloseDrawer();
        }
    }

    /// <summary>
    /// The keyboard model the drawer's own NavigationView gives Compact mode, which the Default
    /// template doesn't: the arrows walk the rows without navigating, Enter opens the one in focus,
    /// Escape puts the menu away. The arrows are handled here, in Preview, so the ListBox never sees
    /// them: left to itself it would move its own selection, which this menu doesn't use. Handled on the
    /// panel rather than the Window so Escape can't beat a grid cell editor's cancel.
    /// </summary>
    private void NavigationMenu_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down or Key.Up:
                MoveMenuFocus(e.Key == Key.Down ? 1 : -1);
                e.Handled = true;
                break;

            case Key.Enter when Keyboard.FocusedElement is ListBoxItem row:
                Navigate(row);
                e.Handled = true;
                break;

            case Key.Escape:
                CloseDrawer();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Steps the keyboard from one destination to the next. It walks <see cref="NavigationMenu"/>'s own
    /// items rather than calling MoveFocus: a ListBox sets KeyboardNavigation.TabNavigation to Once, so
    /// Next/Previous leave the list altogether instead of visiting the sibling rows — which is why the
    /// arrows did nothing useful when this went through MoveFocus. With nothing in the list focused yet
    /// (how the menu opens), Down starts at the top row and Up at the bottom one.
    /// </summary>
    private void MoveMenuFocus(int step)
    {
        var rows = NavigationMenu.Items.OfType<ListBoxItem>().ToList();

        if (rows.Count == 0)
        {
            return;
        }

        var current = rows.FindIndex(row => row.IsKeyboardFocusWithin);
        var next = current < 0
            ? (step > 0 ? 0 : rows.Count - 1)
            : Math.Clamp(current + step, 0, rows.Count - 1);

        rows[next].Focus();
    }

    private void SettingsNavButton_Click(object sender, RoutedEventArgs e) => OpenSettings();

    /// <summary>
    /// Puts the menu away, and takes the keyboard off the rows it hides — focus left on a collapsed
    /// element goes nowhere, and Tab from there starts over at the top of the window. IsOpen = false
    /// rather than ToggleDrawer(): these are unconditional closes, and toggling would *open* a drawer
    /// that happened to be shut.
    /// </summary>
    private void CloseDrawer()
    {
        NavigationDrawer.IsOpen = false;

        if (NavigationMenu.IsKeyboardFocusWithin || SettingsNavButton.IsKeyboardFocused)
        {
            ToggleNavigationButton.Focus();
        }
    }

    // MainTabControl.CloseMode="Hide" (set in XAML) means closing a tab only collapses its
    // TabItemExt rather than removing it from Items, so it's always still there to select —
    // just possibly Visibility.Collapsed from having been closed.
    private void ShowTab(TabItemExt tab)
    {
        tab.Visibility = Visibility.Visible;
        MainTabControl.SelectedItem = tab;
        CloseDrawer();
    }

    /// <summary>
    /// Units of measure, categories, roles and costing defaults, edited in a dialog over the current screen. Once it
    /// closes, the screen on show refreshes so its pickers include anything added or renamed; the other
    /// tabs pick the changes up when they're next shown, as they do for any change.
    /// </summary>
    private void OpenSettings()
    {
        CloseDrawer();

        var settings = new SettingsWindow(
            _unitsOfMeasureViewModel, _categoriesViewModel, _productCategoriesViewModel, _rolesViewModel, _costingDefaultsViewModel) { Owner = this };
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
