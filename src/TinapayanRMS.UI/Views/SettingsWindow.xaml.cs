using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// The lookups every screen picks from, opened from the navigation drawer's Settings item: units of
/// measure, raw material categories and sub-assembly roles, one tab each. Built afresh each time it
/// opens, over the same long-lived ViewModels, so a tab keeps its selection and unsaved edits between
/// visits.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(
        UnitsOfMeasureViewModel unitsOfMeasureViewModel,
        CategoriesViewModel categoriesViewModel,
        SubAssemblyRolesViewModel rolesViewModel)
    {
        InitializeComponent();

        UnitsOfMeasureTab.Content = new UnitsOfMeasureView(unitsOfMeasureViewModel);
        CategoriesTab.Content = new CategoriesView(categoriesViewModel);
        RolesTab.Content = new SubAssemblyRolesView(rolesViewModel);

        // The ViewModels outlive this window, so their delete confirmations are removed with it.
        var confirmDeletes = new CompositeDisposable(
            unitsOfMeasureViewModel.RegisterNativeMessageBoxHandler(),
            categoriesViewModel.RegisterNativeMessageBoxHandler(),
            rolesViewModel.RegisterNativeMessageBoxHandler());
        Closed += (_, _) => confirmDeletes.Dispose();

        // Every list is small, so each opening loads them all fresh rather than per tab.
        Loaded += (_, _) =>
        {
            unitsOfMeasureViewModel.LoadCommand.Execute().Subscribe();
            categoriesViewModel.LoadCommand.Execute().Subscribe();
            rolesViewModel.LoadCommand.Execute().Subscribe();
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
