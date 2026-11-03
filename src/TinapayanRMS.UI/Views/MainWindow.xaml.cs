using System.ComponentModel;
using System.Reactive.Linq;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ReactiveUI;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.ViewModels;
using TinapayanRMS.UI.Vim;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Views;

public sealed partial class MainWindow : AppWindow, IVimHost
{
    // Each tab's screen, until the tab is first shown and the screen is made (see CreateScreen).
    private readonly Dictionary<TabItemExt, Func<FrameworkElement>> _screenFactories = [];

    // Each tab's screen LoadCommand, run whenever that tab is shown (see LoadSelectedTab).
    private readonly Dictionary<TabItemExt, ReactiveCommand<RxVoid, RxVoid>> _loadCommands = [];
    private readonly HashSet<TabItemExt> _loadingTabs = [];

    // The tab that shows each screen, and each tab's screen.
    private readonly Dictionary<AppScreen, TabItemExt> _tabs = [];
    private readonly Dictionary<TabItemExt, AppScreen> _screens = [];

    // Moving between the screens, with their unsaved changes asked about on the way. It knows screens; this window knows tabs.
    private readonly ScreenNavigator _navigator;

    // Set while a change of tab is undone to ask about the unsaved changes of the screen it left (TurnBackToUnsaved).
    private bool _isTurningBack;

    // Set once every screen's unsaved changes are saved or thrown away, so the close that follows goes ahead.
    private bool _isCloseConfirmed;

    private readonly UnitsOfMeasureViewModel _unitsOfMeasureViewModel;
    private readonly CategoriesViewModel _categoriesViewModel;
    private readonly ProductCategoriesViewModel _productCategoriesViewModel;
    private readonly SubAssemblyRolesViewModel _rolesViewModel;
    private readonly CostingDefaultsViewModel _costingDefaultsViewModel;
    private readonly KeyboardSettingsViewModel _keyboardSettingsViewModel;
    private readonly DatabaseBackupsViewModel _databaseBackupsViewModel;
    private readonly QuickFindViewModel _quickFindViewModel;

    // Opens each screen's record by its id, for Find: the open that asks about its own unsaved changes.
    private readonly Dictionary<AppScreen, Func<int, Task<bool>>> _recordOpeners = [];

    public MainWindow(
        MainWindowViewModel viewModel,
        RawMaterialsViewModel rawMaterialsViewModel,
        SubAssembliesViewModel subAssembliesViewModel,
        ProductsViewModel productsViewModel,
        ReportsViewModel reportsViewModel,
        PriceSimulatorViewModel priceSimulatorViewModel,
        UnitsOfMeasureViewModel unitsOfMeasureViewModel,
        CategoriesViewModel categoriesViewModel,
        ProductCategoriesViewModel productCategoriesViewModel,
        SubAssemblyRolesViewModel rolesViewModel,
        CostingDefaultsViewModel costingDefaultsViewModel,
        KeyboardSettingsViewModel keyboardSettingsViewModel,
        DatabaseBackupsViewModel databaseBackupsViewModel,
        PrintTabViewModel printTabViewModel,
        QuickFindViewModel quickFindViewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _quickFindViewModel = quickFindViewModel;
        _unitsOfMeasureViewModel = unitsOfMeasureViewModel;
        _categoriesViewModel = categoriesViewModel;
        _productCategoriesViewModel = productCategoriesViewModel;
        _rolesViewModel = rolesViewModel;
        _costingDefaultsViewModel = costingDefaultsViewModel;
        _keyboardSettingsViewModel = keyboardSettingsViewModel;
        _databaseBackupsViewModel = databaseBackupsViewModel;

        _screenFactories[RawMaterialsTab] = () => new RawMaterialsView(rawMaterialsViewModel);
        _screenFactories[SubAssembliesTab] = () => new SubAssembliesView(subAssembliesViewModel);
        _screenFactories[ProductsTab] = () => new ProductsView(productsViewModel);
        _screenFactories[ReportsTab] = () => new ReportsView(reportsViewModel);
        _screenFactories[PriceSimulatorTab] = () => new PriceSimulatorView(priceSimulatorViewModel);
        _screenFactories[PrintTab] = () => new ProductPrintView(printTabViewModel);
        Closed += (_, _) => (PrintTab.Content as ProductPrintView)?.Dispose();

        // The tab shown at startup: the first one open. Made here rather than on SelectionChanged, since the tab control
        // can take in the selected tab's content before it raises that.
        CreateScreen(MainTabControl.Items.OfType<TabItemExt>().First(tab => tab.Visibility == Visibility.Visible));

        RawMaterialsNavItem.Tag = RawMaterialsTab;
        SubAssembliesNavItem.Tag = SubAssembliesTab;
        ProductsNavItem.Tag = ProductsTab;
        ReportsNavItem.Tag = ReportsTab;
        PriceSimulatorNavItem.Tag = PriceSimulatorTab;

        _loadCommands[RawMaterialsTab] = rawMaterialsViewModel.LoadCommand;
        _loadCommands[SubAssembliesTab] = subAssembliesViewModel.LoadCommand;
        _loadCommands[ProductsTab] = productsViewModel.LoadCommand;
        _loadCommands[ReportsTab] = reportsViewModel.LoadCommand;
        _loadCommands[PriceSimulatorTab] = priceSimulatorViewModel.LoadCommand;

        // Print's load prepares its pages again: This product as Products shows it now, so the tab follows the edits made
        // there, and the product opened there, since it was last shown (the user's choice, 2026-09-27); a set as it's saved.
        _loadCommands[PrintTab] = printTabViewModel.LoadCommand;

        _tabs[AppScreen.RawMaterials] = RawMaterialsTab;
        _tabs[AppScreen.SubAssemblies] = SubAssembliesTab;
        _tabs[AppScreen.Products] = ProductsTab;
        _tabs[AppScreen.Reports] = ReportsTab;
        _tabs[AppScreen.PriceSimulator] = PriceSimulatorTab;
        _tabs[AppScreen.Print] = PrintTab;
        foreach (var (screen, tab) in _tabs)
        {
            _screens[tab] = screen;
        }

        _navigator = new ScreenNavigator(
            viewModel.Navigation,
            [
                new(AppScreen.RawMaterials, () => rawMaterialsViewModel.SelectedRawMaterial?.Id, rawMaterialsViewModel.OpenRawMaterial, rawMaterialsViewModel),
                new(AppScreen.SubAssemblies, () => subAssembliesViewModel.SelectedRow?.Id, subAssembliesViewModel.OpenSubAssembly, subAssembliesViewModel),
                new(AppScreen.Products, () => productsViewModel.SelectedProduct?.Id, productsViewModel.OpenProduct, productsViewModel),
                new(AppScreen.Reports, () => null),
                new(AppScreen.PriceSimulator, () => null),
                new(AppScreen.Print, () => null, UnsavedChanges: productsViewModel),
            ],
            () => MainTabControl.SelectedItem is TabItemExt shown && _screens.TryGetValue(shown, out var screen) ? screen : null,
            screen => ShowTab(_tabs[screen]),
            ShowFailure);

        // A Print on Products (the header's, its open form applied, or a folder's or the list's): the Print tab with that
        // choice, which it prepares as it's shown.
        productsViewModel.PrintRequests.Subscribe(scope =>
        {
            printTabViewModel.Pick(scope);
            ShowTab(PrintTab);
        });

        // A Used By tile on Sub-Assemblies or Raw Materials, or a double-clicked row of Reports' Product Cost Summary or the Price Simulator, opens its
        // product on Products (OpenOnAsync).
        subAssembliesViewModel.ProductOpenRequests
            .Merge(rawMaterialsViewModel.ProductOpenRequests)
            .Merge(reportsViewModel.ProductOpenRequests)
            .Merge(priceSimulatorViewModel.ProductOpenRequests)
            .Subscribe(productId => _ = _navigator.OpenOnAsync(AppScreen.Products, () => productsViewModel.OpenProductAsync(productId)));

        // Sub-Assemblies' New Product starts a new product on Products that links the sub-assembly. It waits for the load
        // that showing the tab starts, so the sub-assembly is found as it's saved now.
        subAssembliesViewModel.NewProductRequests.Subscribe(subAssemblyId =>
            _ = _navigator.OpenOnAsync(AppScreen.Products, () => productsViewModel.StartNewProductFromAsync(subAssemblyId)));

        // A double-clicked row of Reports' Raw Materials Price Summary, or a made ingredient's Used By tile on Sub-Assemblies,
        // opens its material on Raw Materials, the same way.
        reportsViewModel.RawMaterialOpenRequests
            .Merge(subAssembliesViewModel.RawMaterialOpenRequests)
            .Subscribe(rawMaterialId => _ = _navigator.OpenOnAsync(AppScreen.RawMaterials, () => rawMaterialsViewModel.OpenRawMaterialAsync(rawMaterialId)));

        // And the other way: a linked badge on Products, or a made ingredient's Recipe card on Raw Materials, opens its
        // standalone sub-assembly on Sub-Assemblies.
        productsViewModel.SubAssemblyOpenRequests
            .Merge(rawMaterialsViewModel.SubAssemblyOpenRequests)
            .Subscribe(subAssemblyId => _ = _navigator.OpenOnAsync(AppScreen.SubAssemblies, () => subAssembliesViewModel.OpenSubAssemblyAsync(subAssemblyId)));

        _recordOpeners[AppScreen.RawMaterials] = rawMaterialsViewModel.OpenRawMaterialAsync;
        _recordOpeners[AppScreen.SubAssemblies] = subAssembliesViewModel.OpenSubAssemblyAsync;
        _recordOpeners[AppScreen.Products] = productsViewModel.OpenProductAsync;

        // The tab that's selected at startup; every later switch goes through SelectionChanged.
        Loaded += (_, _) =>
        {
            _navigator.Start();
            LoadSelectedTab();
        };
    }

    /// <summary>The Vim keys' <c>gt</c> and <c>gT</c>: the next screen's tab or the one before.</summary>
    public void StepTab(int step) => VimTabs.Step(MainTabControl, step);

    /// <summary>The Vim keys' <c>H</c>, as Alt+Left.</summary>
    public bool GoBack() => _navigator.GoBack();

    /// <summary>The Vim keys' <c>L</c>, as Alt+Right.</summary>
    public bool GoForward() => _navigator.GoForward();

    /// <summary>The command line's <c>:raw</c>, <c>:products</c> and the rest: the screen, with the keyboard on its tab.</summary>
    public bool ShowScreen(AppScreen screen)
    {
        if (!_tabs.TryGetValue(screen, out var tab))
        {
            return false;
        }

        ShowTab(tab);
        tab.Focus();
        return true;
    }

    /// <summary>The command line's <c>:settings</c>.</summary>
    bool IVimHost.OpenSettings()
    {
        OpenSettings();
        return true;
    }

    /// <summary>The screen on show, for a command given from the tab strip.</summary>
    public FrameworkElement? CurrentScreen => (MainTabControl.SelectedItem as TabItemExt)?.Content as FrameworkElement;

    private void BackButton_Click(object sender, RoutedEventArgs e) => _navigator.GoBack();

    private void FindButton_Click(object sender, RoutedEventArgs e) => OpenFind();

    /// <summary>
    /// Find (Ctrl+K, or the title bar's magnifier): the record picked opens on its own screen, as a link from another screen
    /// opens one, the screen showing asked about its unsaved changes first (OpenOnAsync).
    /// </summary>
    private void OpenFind()
    {
        CloseDrawer();
        if (QuickFindWindow.Pick(_quickFindViewModel, this) is { } picked && _recordOpeners.TryGetValue(picked.Screen, out var open))
        {
            _ = _navigator.OpenOnAsync(picked.Screen, () => open(picked.Id));
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e) => _navigator.GoForward();

    /// <summary>
    /// Alt+Left and Alt+Right, as a browser and File Explorer have them. On the window's tunnelling PreviewKeyDown, so
    /// a grid or box that has the cursor can't take the key first. With Alt held WPF reports the key as Key.System,
    /// the real one in SystemKey. The key is only marked handled when there was somewhere to go.
    /// </summary>
    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+K: Find, from anywhere in the window, a grid or a box included (2026-10-29).
        if (e.Key == Key.K && e.KeyboardDevice.Modifiers == ModifierKeys.Control)
        {
            OpenFind();
            e.Handled = true;
            return;
        }

        if (e.Key != Key.System || e.KeyboardDevice.Modifiers != ModifierKeys.Alt)
        {
            return;
        }

        if (e.SystemKey is Key.Left or Key.Right)
        {
            e.Handled = e.SystemKey == Key.Left ? _navigator.GoBack() : _navigator.GoForward();
        }
    }

    /// <summary>
    /// A move between screens that failed, with no screen of its own to say so on. It goes back on the dispatcher, where a
    /// failure in a tab picked directly already is, so App's handler says what went wrong and the app carries on.
    /// </summary>
    private void ShowFailure(Exception exception) =>
        Dispatcher.BeginInvoke(() => ExceptionDispatchInfo.Throw(exception), DispatcherPriority.Normal);

    /// <summary>
    /// A tab picked (the tab strip, the menu, gt, a command, or the tab showing closed with its ✕) while the screen left holds
    /// unsaved changes: the screen left shows again at once, and the tab picked once the changes are saved or thrown away.
    /// Don't Save puts the screen back as it was saved, so they aren't there to come back to. TabControlExt has no change of
    /// selection to cancel, so it's undone instead; a tab closed opens again until then. False when nothing unsaved is left.
    /// </summary>
    private bool TurnBackToUnsaved(SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.OfType<TabItemExt>().FirstOrDefault() is not { } leftTab
            || e.AddedItems.OfType<TabItemExt>().FirstOrDefault() is not { } pickedTab
            || _navigator.UnsavedChangesLeft(_screens[leftTab], _screens[pickedTab]) is not { } form)
        {
            return false;
        }

        var wasClosed = leftTab.Visibility != Visibility.Visible;
        _isTurningBack = true;
        try
        {
            leftTab.Visibility = Visibility.Visible;
            MainTabControl.SelectedItem = leftTab;
        }
        finally
        {
            _isTurningBack = false;
        }

        // Asked once the tab control is done changing its selection.
        Dispatcher.InvokeAsync(() => _navigator.AfterAskingAsync(form, () =>
        {
            ShowTab(pickedTab);
            if (wasClosed)
            {
                leftTab.Visibility = Visibility.Collapsed;
            }
        }));

        return true;
    }

    /// <summary>
    /// Closing the app with unsaved changes asks about them first (ScreenNavigator.CloseAfterAskingAsync), and a Cancel keeps
    /// it open. Not when it's closing to start again after a restore, which already said they'd be lost.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel
            || _isCloseConfirmed
            || Application.Current is App { IsRestarting: true }
            || !_navigator.HasUnsavedChanges)
        {
            return;
        }

        e.Cancel = true;
        Dispatcher.InvokeAsync(() => _navigator.CloseAfterAskingAsync(() =>
        {
            _isCloseConfirmed = true;
            Close();
        }));
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
    /// Units of measure, categories, roles, costing defaults and the keyboard, edited in a dialog over the current screen. Once it
    /// closes, the screen on show refreshes so its pickers include anything added or renamed; the other
    /// tabs pick the changes up when they're next shown, as they do for any change.
    /// </summary>
    private void OpenSettings()
    {
        CloseDrawer();

        var settings = new SettingsWindow(
            _unitsOfMeasureViewModel, _categoriesViewModel, _productCategoriesViewModel, _rolesViewModel, _costingDefaultsViewModel,
            _keyboardSettingsViewModel, _databaseBackupsViewModel) { Owner = this };
        SfSkinManager.SetTheme(settings, SfSkinManager.GetTheme(this));
        settings.ShowDialog();

        LoadSelectedTab();
    }

    private void MainTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles, so a grid or picker inside a tab raises it here too. A change undone by TurnBackToUnsaved
        // is no move: the screen left never stopped showing.
        if (!ReferenceEquals(e.OriginalSource, MainTabControl) || _isTurningBack || (IsLoaded && TurnBackToUnsaved(e)))
        {
            return;
        }

        // Before TabControlExt takes the tab's content in, which it does once this event has been raised.
        if (MainTabControl.SelectedItem is TabItemExt shown)
        {
            CreateScreen(shown);
        }

        if (IsLoaded)
        {
            RecordHistoryStep(e);
            LoadSelectedTab();
        }
    }

    /// <summary>
    /// Makes a tab's screen the first time the tab is shown, and keeps it from then on. Making all seven at startup took
    /// most of the time between the window appearing and the app being ready (2026-10-22), and a session seldom opens
    /// them all. A screen's ViewModel exists from the start, so a link from another screen can open a record on one
    /// that isn't made yet: the screen shows it once it's made and loaded.
    /// </summary>
    private void CreateScreen(TabItemExt tab)
    {
        if (_screenFactories.Remove(tab, out var create))
        {
            tab.Content = create();
        }
    }

    /// <summary>
    /// Every change of screen that isn't Back or Forward itself — the menu, the tab strip, a tab closed, a link from
    /// another screen — is a step: the screen left goes into the history with the record it has open as it's left.
    /// </summary>
    private void RecordHistoryStep(SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.OfType<TabItemExt>().FirstOrDefault() is { } leftTab
            && e.AddedItems.OfType<TabItemExt>().FirstOrDefault() is { } shownTab)
        {
            _navigator.RecordStep(_screens[leftTab], _screens[shownTab]);
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

        // A load shows its own failure in its screen's error notice (ViewModelBase.LoadSafelyAsync). Should one escape it
        // all the same, ReactiveUI hands it to App's handler, which says what went wrong; the error on Execute's own
        // observable is the same failure again, and without a handler here it would be thrown a second time.
        load.Execute().Finally(() => _loadingTabs.Remove(tab)).Subscribe(_ => { }, _ => { });
    }
}
