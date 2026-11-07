using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.Pricing;
using TinapayanRMS.UI.Exporting;
using TinapayanRMS.Presentation.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Views;

/// <summary>The Price Simulator screen: price changes to try, and every product's figures with them.</summary>
public sealed partial class PriceSimulatorView : UserControl
{
    public PriceSimulatorView(PriceSimulatorViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // The rows picked are the ViewModel's, for Print Report. Handed over here rather than bound: the grid gives itself a
        // collection of its own as it's initialized, while a binding still reads null, and that replaces the binding.
        ProductsGrid.SelectedItems = viewModel.SelectedProducts;
        viewModel.RegisterNativeMessageBoxHandler();

        // A scenario's name for Save Scenario, after the button's click is over, as Sub-Assemblies asks for a name.
        viewModel.PromptForName.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var request = interaction.Input;
            interaction.SetOutput(TextPromptWindow.Prompt(request.Title, request.Label, request.InitialValue, Window.GetWindow(this), request.Message));
        });

        // The products as the table shows them, and the changes that made them. The sheet says when the figures are unbuffered.
        viewModel.RegisterExcelExport(this, _ =>
        [
            new ExcelSheet(viewModel.IsUnbuffered ? "Products (Unbuffered)" : "Products", ProductsGrid),
            new ExcelSheet("Price Changes", ChangesGrid),
        ]);

        // The report prints the products as the table shows them: its column filters applied, in its sort order.
        viewModel.ShownProducts = () => ProductsGrid.View is { } view
            ? [.. view.Records.Select(record => record.Data).OfType<PriceSimulatorProductRow>()]
            : viewModel.Products;

        // The report in a print pane of its own, themed as the main window is, after the button's click is over.
        viewModel.ShowPrint.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var owner = Window.GetWindow(this);
            var window = new PrintPreviewWindow(interaction.Input, SfSkinManager.GetTheme(owner)) { Owner = owner };
            window.ShowDialog();
            interaction.SetOutput(RxVoid.Default);
        });
    }

    /// <summary>Opens the double-clicked product once the press is over, as Reports does (ReportsView explains why).</summary>
    private void Products_CellDoubleTapped(object? sender, GridCellDoubleTappedEventArgs e)
    {
        if (e.Record is PriceSimulatorProductRow row && DataContext is PriceSimulatorViewModel viewModel)
        {
            Dispatcher.BeginInvoke(() => viewModel.OpenProductCommand.Execute(row.ProductId).Subscribe(), DispatcherPriority.Input);
        }
    }
}
