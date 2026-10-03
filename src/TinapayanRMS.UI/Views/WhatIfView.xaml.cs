using System.Windows.Controls;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.Exporting;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>The What-If Prices screen: price changes to try, and every product's figures with them.</summary>
public partial class WhatIfView : UserControl
{
    public WhatIfView(WhatIfViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();

        // The products as the table shows them, and the changes that made them.
        viewModel.RegisterExcelExport(this, _ => [new ExcelSheet("Products", ProductsGrid), new ExcelSheet("Price Changes", ChangesGrid)]);
    }

    /// <summary>Opens the double-clicked product once the press is over, as Reports does (ReportsView explains why).</summary>
    private void Products_CellDoubleTapped(object? sender, GridCellDoubleTappedEventArgs e)
    {
        if (e.Record is WhatIfProductRow row && DataContext is WhatIfViewModel viewModel)
        {
            Dispatcher.BeginInvoke(() => viewModel.OpenProductCommand.Execute(row.ProductId).Subscribe(), DispatcherPriority.Input);
        }
    }
}
