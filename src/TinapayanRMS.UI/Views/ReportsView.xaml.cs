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

        // Every column can be hidden but Product, which says what a row is.
        ColumnChoices.ItemsSource = ProductCostsGrid.Columns
            .Where(column => column.MappingName != nameof(ProductCostRow.ProductName))
            .ToList();
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

    /// <summary>Opens the double-clicked raw material on Raw Materials, once the press is over, as a product opens on Products.</summary>
    private void RawMaterialPrices_CellDoubleTapped(object? sender, GridCellDoubleTappedEventArgs e)
    {
        if (e.Record is RawMaterialPriceSummaryRow row && DataContext is ReportsViewModel viewModel)
        {
            Dispatcher.BeginInvoke(() => viewModel.OpenRawMaterialCommand.Execute(row.RawMaterialId).Subscribe(), DispatcherPriority.Input);
        }
    }
}
