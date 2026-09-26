using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ReactiveUI;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Views;

public partial class MainWindow : ChromelessWindow
{
    // Each tab's screen LoadCommand, run whenever that tab is shown (see LoadSelectedTab).
    private readonly Dictionary<TabItemExt, ReactiveCommand<RxVoid, RxVoid>> _loadCommands = [];
    private readonly HashSet<TabItemExt> _loadingTabs = [];

    // Each tab's screen as Back and Forward see it: which one it is, the record it has open, how to reopen one, and
    // whether its form holds unsaved changes. Reports has no record to reopen.
    private readonly Dictionary<TabItemExt, HistoryScreen> _historyScreens = [];

    private readonly NavigationHistory _navigation;

    // Set while Back or Forward shows a tab, so the switch isn't recorded as a new step.
    private bool _isRestoringHistory;

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

        _navigation = viewModel.Navigation;
        _historyScreens[RawMaterialsTab] = new(AppScreen.RawMaterials,
            () => rawMaterialsViewModel.SelectedRawMaterial?.Id, rawMaterialsViewModel.OpenRawMaterial, () => rawMaterialsViewModel.History.IsDirty);
        _historyScreens[SubAssembliesTab] = new(AppScreen.SubAssemblies,
            () => subAssembliesViewModel.SelectedRow?.Id, subAssembliesViewModel.OpenSubAssembly, () => subAssembliesViewModel.History.IsDirty);
        _historyScreens[ProductsTab] = new(AppScreen.Products,
            () => productsViewModel.SelectedProduct?.Id, productsViewModel.OpenProduct, () => productsViewModel.History.IsDirty);
        _historyScreens[ReportsTab] = new(AppScreen.Reports, () => null, null, () => false);

        // A Used By tile on Sub-Assemblies or Raw Materials opens its product on Products. Picked before the tab shows, so a list
        // not loaded yet has the product waiting for the load that showing the tab starts.
        subAssembliesViewModel.ProductOpenRequests.Merge(rawMaterialsViewModel.ProductOpenRequests).Subscribe(productId =>
        {
            productsViewModel.OpenProduct(productId);
            ShowTab(ProductsTab);
        });

        // And the other way: a linked badge on Products opens its standalone sub-assembly on Sub-Assemblies.
        productsViewModel.SubAssemblyOpenRequests.Subscribe(subAssemblyId =>
        {
            subAssembliesViewModel.OpenSubAssembly(subAssemblyId);
            ShowTab(SubAssembliesTab);
        });

        // The tab that's selected at startup; every later switch goes through SelectionChanged.
        Loaded += (_, _) =>
        {
            if (MainTabControl.SelectedItem is TabItemExt tab && _historyScreens.TryGetValue(tab, out var screen))
            {
                _navigation.Start(screen.Visit());
            }

            LoadSelectedTab();
        };
    }

    /// <summary>
    /// UseNativeChrome (MainWindow.xaml) gives the window a WindowChrome whose resize band ChromelessWindow binds to its
    /// own ResizeBorderThickness. That's 0 here, since it's also the width of the border the theme draws. A local value
    /// replaces the binding, so Windows resizes from a band as wide as a normal window's frame while no border is drawn.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        // Before base, which adds ChromelessWindow's own hook: HwndSource calls the newest hook first, so this one runs
        // after it and gets the last word on the region.
        if (PresentationSource.FromVisual(this) is HwndSource source)
        {
            source.AddHook(DropWindowRegion);
        }

        base.OnSourceInitialized(e);

        ChromelessWindowDpi.LetWpfHandleDpiChanges(this);

        if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
        {
            chrome.ResizeBorderThickness = SystemParameters.WindowResizeBorderThickness;
        }

        // Windows 11 rounds a window like this one only when asked: it stopped doing it by itself once ChromelessWindow
        // had given the window a region (below), even after the region was gone again. Windows 10 has no such attribute
        // and returns an error, which leaves its square corners as they are.
        var hwnd = new WindowInteropHelper(this).Handle;
        var round = DwmCornerRound;
        _ = DwmSetWindowAttribute(hwnd, DwmWindowCornerPreference, ref round, sizeof(int));
        _ = SetWindowRgn(hwnd, IntPtr.Zero, true);
    }

    private const int DwmWindowCornerPreference = 33;
    private const int DwmCornerRound = 2;

    private const int WmSize = 0x0005;
    private const int WmSizing = 0x0214;
    private const int WmExitSizeMove = 0x0232;

    // Set while a deferred DropWindowRegion clear is queued, so a live resize's burst of messages queues just one.
    private bool _regionClearQueued;

    /// <summary>
    /// Keeps Windows 11's rounded corners. Whenever the window is sized, finishes a move or changes state, ChromelessWindow
    /// gives it a square window region: a leftover of its own mode, which cuts its corners with one. Windows doesn't round a
    /// window that has a region, so the corners went square after any drag, such as onto the secondary monitor, and after
    /// a restore. ChromelessWindow sets it from two places: its own hook (WM_SIZE, WM_SIZING, WM_EXITSIZEMOVE), which has
    /// just run when this one does, and its StateChanged handler, which WPF raises from WM_SIZE after every hook. So the
    /// region comes off now and again once this message has been dealt with.
    /// </summary>
    private IntPtr DropWindowRegion(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg is WmSize or WmSizing or WmExitSizeMove)
        {
            _ = SetWindowRgn(hwnd, IntPtr.Zero, true);

            if (!_regionClearQueued)
            {
                _regionClearQueued = true;
                Dispatcher.BeginInvoke(() =>
                {
                    _regionClearQueued = false;
                    _ = SetWindowRgn(hwnd, IntPtr.Zero, true);
                });
            }
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool redraw);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private void BackButton_Click(object sender, RoutedEventArgs e) => NavigateHistory(back: true);

    private void ForwardButton_Click(object sender, RoutedEventArgs e) => NavigateHistory(back: false);

    /// <summary>
    /// Alt+Left and Alt+Right, as a browser and File Explorer have them. On the window's tunnelling PreviewKeyDown, so
    /// a grid or box that has the cursor can't take the key first. With Alt held WPF reports the key as Key.System,
    /// the real one in SystemKey. The key is only marked handled when there was somewhere to go.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.System || e.KeyboardDevice.Modifiers != ModifierKeys.Alt)
        {
            return;
        }

        if (e.SystemKey is Key.Left or Key.Right)
        {
            e.Handled = NavigateHistory(back: e.SystemKey == Key.Left);
        }
    }

    /// <summary>
    /// Goes back or forward one screen, and reopens the record that screen had open — unless its form holds unsaved
    /// changes, which Back never throws away: the screen then shows as it is. The record is asked for before the tab
    /// shows, as the links between screens do, so a list not loaded yet opens it once the load that showing the tab
    /// starts brings it in. False when there was nowhere to go.
    /// </summary>
    private bool NavigateHistory(bool back)
    {
        if (MainTabControl.SelectedItem is not TabItemExt currentTab || !_historyScreens.TryGetValue(currentTab, out var current))
        {
            return false;
        }

        var leaving = current.Visit();
        if ((back ? _navigation.GoBack(leaving) : _navigation.GoForward(leaving)) is not { } target)
        {
            return false;
        }

        var (targetTab, screen) = _historyScreens.First(pair => pair.Value.Screen == target.Screen);
        if (target.RecordId is { } recordId && screen.Open is { } open && screen.CurrentRecord() != recordId && !screen.IsDirty())
        {
            open(recordId);
        }

        _isRestoringHistory = true;
        try
        {
            ShowTab(targetTab);
        }
        finally
        {
            _isRestoringHistory = false;
        }

        return true;
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
            RecordHistoryStep(e);
            LoadSelectedTab();
        }
    }

    /// <summary>
    /// Every change of screen that isn't Back or Forward itself — the menu, the tab strip, a tab closed, a link from
    /// another screen — is a step: the screen left goes into the history with the record it has open as it's left.
    /// </summary>
    private void RecordHistoryStep(SelectionChangedEventArgs e)
    {
        if (_isRestoringHistory
            || e.RemovedItems.OfType<TabItemExt>().FirstOrDefault() is not { } leftTab
            || e.AddedItems.OfType<TabItemExt>().FirstOrDefault() is not { } shownTab
            || !_historyScreens.TryGetValue(leftTab, out var left)
            || !_historyScreens.TryGetValue(shownTab, out var shown))
        {
            return;
        }

        _navigation.Visit(left.Visit(), shown.Screen);
    }

    /// <summary>A tab's screen as <see cref="NavigationHistory"/> sees it; <paramref name="Open"/> is null for one with no record to reopen.</summary>
    private sealed record HistoryScreen(AppScreen Screen, Func<int?> CurrentRecord, Action<int>? Open, Func<bool> IsDirty)
    {
        /// <summary>The screen with the record it has open now.</summary>
        public ScreenVisit Visit() => new(Screen, CurrentRecord());
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
