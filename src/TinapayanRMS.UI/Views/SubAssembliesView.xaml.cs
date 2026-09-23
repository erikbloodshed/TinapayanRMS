using System.ComponentModel;
using System.Windows.Controls;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.ScrollAxis;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

public partial class SubAssembliesView : UserControl
{
    public SubAssembliesView(SubAssembliesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();

        // A sub-assembly picked from elsewhere (a linked badge on Products) may be scrolled out of sight, as in
        // ProductListCard: the grid doesn't bring a SelectedItem set through its binding into view itself.
        DependencyPropertyDescriptor.FromProperty(SfDataGrid.SelectedItemProperty, typeof(SfDataGrid))
            .AddValueChanged(List, (_, _) =>
            {
                if (List.SelectedItem is { } row && List.ResolveToRowIndex(row) is var rowIndex and >= 0)
                {
                    List.ScrollInView(new RowColumnIndex(rowIndex, 0));
                }
            });
    }
}
