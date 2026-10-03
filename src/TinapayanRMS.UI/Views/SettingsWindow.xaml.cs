using System.ComponentModel;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Data;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.Windows.Controls.Input;
using Syncfusion.Windows.Tools.Controls;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.ViewModels;
using TinapayanRMS.UI.Vim;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// The lookups every screen picks from, opened from the navigation drawer's Settings item: units of
/// measure, raw material categories, product categories, sub-assembly roles and the costing a new product starts
/// with, one tab each, the Keyboard tab's Vim keys, and the Database tab's backups. Built afresh each time it
/// opens, over the same long-lived ViewModels, so a tab keeps its selection and unsaved edits between
/// visits.
/// </summary>
public partial class SettingsWindow : AppWindow, IVimHost
{
    public SettingsWindow(
        UnitsOfMeasureViewModel unitsOfMeasureViewModel,
        CategoriesViewModel categoriesViewModel,
        ProductCategoriesViewModel productCategoriesViewModel,
        SubAssemblyRolesViewModel rolesViewModel,
        CostingDefaultsViewModel costingDefaultsViewModel,
        KeyboardSettingsViewModel keyboardSettingsViewModel,
        DatabaseBackupsViewModel databaseBackupsViewModel)
    {
        InitializeComponent();

        UnitsOfMeasureTab.Content = new UnitsOfMeasureView(unitsOfMeasureViewModel);
        CategoriesTab.Content = new CategoriesView(categoriesViewModel);
        ProductCategoriesTab.Content = new ProductCategoriesView(productCategoriesViewModel);
        RolesTab.Content = new SubAssemblyRolesView(rolesViewModel);
        CostingDefaultsTab.Content = new CostingDefaultsView(costingDefaultsViewModel);
        KeyboardTab.Content = new KeyboardSettingsView(keyboardSettingsViewModel);
        var databaseView = new DatabaseBackupsView(databaseBackupsViewModel);
        DatabaseTab.Content = databaseView;

        // The ViewModels and the main window outlive this window, so their delete confirmations are removed with it, and
        // so is everything that ties its controls to them.
        var confirmDeletes = new CompositeDisposable(
            unitsOfMeasureViewModel.RegisterNativeMessageBoxHandler(),
            categoriesViewModel.RegisterNativeMessageBoxHandler(),
            productCategoriesViewModel.RegisterNativeMessageBoxHandler(),
            rolesViewModel.RegisterNativeMessageBoxHandler(),
            databaseView);
        Closed += (_, _) =>
        {
            confirmDeletes.Dispose();
            ReleaseControls();
        };

        // Every list is small, so each opening loads them all fresh rather than per tab. Each load shows its own failure
        // in its tab's error notice, as the main window's tabs do (MainWindow.LoadSelectedTab).
        Loaded += (_, _) =>
        {
            unitsOfMeasureViewModel.LoadCommand.Execute().Subscribe(_ => { }, _ => { });
            categoriesViewModel.LoadCommand.Execute().Subscribe(_ => { }, _ => { });
            productCategoriesViewModel.LoadCommand.Execute().Subscribe(_ => { }, _ => { });
            rolesViewModel.LoadCommand.Execute().Subscribe(_ => { }, _ => { });
            costingDefaultsViewModel.LoadCommand.Execute().Subscribe(_ => { }, _ => { });
            databaseBackupsViewModel.LoadCommand.Execute().Subscribe(_ => { }, _ => { });
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    /// <summary>The Vim keys' <c>gt</c> and <c>gT</c>: the next tab or the one before, round from the last to the first.</summary>
    public void StepTab(int step) => VimTabs.Step(SettingsTabs, step);

    /// <summary>
    /// Unties this window's controls from what outlives it. Each of these kept every closed Settings in memory:
    /// <list type="bullet">
    /// <item>An SfDataGrid's view follows its ViewModel list's changes and holds the grid until the grid is disposed;
    /// unloading it isn't enough.</item>
    /// <item>A picker that filters as you type (ComboBoxAdv, as App makes every one) adds its filter to its list's
    /// shared default view, and takes it off only while it's loaded, which a closed window's pickers never are again.</item>
    /// <item>An SfTextBoxExt that loads before its template has found this window (every tab's Name box but the first
    /// tab's) hooks the main window's MouseDown, then unhooks from this window instead. Only Dispose unhooks the main
    /// window's.</item>
    /// </list>
    /// Bindings are cleared before a control is disposed (a grid's selection, a text box's all), so nothing it resets on
    /// the way out reaches the ViewModel: a tab keeps its selection and unsaved edits for next time. The walk is
    /// logical, since only the tab showing is in the visual tree.
    /// </summary>
    private void ReleaseControls()
    {
        foreach (var element in VisualTreeWalk.SelfAndLogicalDescendants(this).ToList())
        {
            switch (element)
            {
                case SfDataGrid grid:
                    BindingOperations.ClearBinding(grid, SfDataGrid.SelectedItemProperty);
                    grid.Dispose();
                    break;
                case SfTextBoxExt box:
                    BindingOperations.ClearAllBindings(box);
                    box.Dispose();
                    break;
                case ComboBoxAdv { ItemsSource: { } items } picker:
                    RemoveFilter(CollectionViewSource.GetDefaultView(items), picker);
                    break;
            }
        }
    }

    /// <summary>Takes <paramref name="picker"/>'s own filter off <paramref name="view"/>, leaving any other's.</summary>
    private static void RemoveFilter(ICollectionView view, ComboBoxAdv picker)
    {
        var filter = view.Filter;
        var removed = false;
        foreach (var part in filter?.GetInvocationList() ?? [])
        {
            if (ReferenceEquals(part.Target, picker))
            {
                filter = (Predicate<object>?)Delegate.Remove(filter, part);
                removed = true;
            }
        }

        if (removed)
        {
            view.Filter = filter;
        }
    }
}
