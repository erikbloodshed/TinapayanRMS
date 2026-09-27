using System.Windows.Controls;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

public partial class ReportsView : UserControl
{
    public ReportsView(ReportsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Opens the double-clicked product, once the second press is over. The grid raises this in the middle of that press,
    /// which goes on bubbling after the handler. Switching screens there takes this view off the window, so the rest of
    /// the press reaches the Reports tab through its logical parent, and TabItem takes a press as a click on its header
    /// and selects Reports again. Products flashed and went.
    /// </summary>
    private void ProductCosts_CellDoubleTapped(object? sender, GridCellDoubleTappedEventArgs e)
    {
        if (e.Record is ProductCostRow row && DataContext is ReportsViewModel viewModel)
        {
            Dispatcher.BeginInvoke(() => viewModel.OpenProductCommand.Execute(row.ProductId).Subscribe(), DispatcherPriority.Input);
        }
    }
}
