using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Windows;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// The lookups every screen picks from, opened from the navigation drawer's Settings item: units of
/// measure and raw material categories, one tab each. Built afresh each time it opens, over the same
/// long-lived ViewModels, so a tab keeps its selection and unsaved edits between visits.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow(UnitsOfMeasureViewModel unitsOfMeasureViewModel, CategoriesViewModel categoriesViewModel)
    {
        InitializeComponent();

        UnitsOfMeasureTab.Content = new UnitsOfMeasureView(unitsOfMeasureViewModel);
        CategoriesTab.Content = new CategoriesView(categoriesViewModel);

        // The ViewModels outlive this window, so their delete confirmations are removed with it.
        var confirmDeletes = new CompositeDisposable(
            unitsOfMeasureViewModel.RegisterNativeMessageBoxHandler(),
            categoriesViewModel.RegisterNativeMessageBoxHandler());
        Closed += (_, _) => confirmDeletes.Dispose();

        // Both lists are small, so each opening loads them fresh rather than per tab.
        Loaded += (_, _) =>
        {
            unitsOfMeasureViewModel.LoadCommand.Execute().Subscribe();
            categoriesViewModel.LoadCommand.Execute().Subscribe();
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
