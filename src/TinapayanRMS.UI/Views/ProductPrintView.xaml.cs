using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReactiveUI;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// The main window's Print tab: Syncfusion's print pane (PrintPreview) over what <see cref="PrintTabViewModel"/> prepared,
/// the product the Products screen has open or a set of products, with the printer, copies, pages, orientation, paper
/// and margins on its left and the pages as they'll print on its right, and under it what to print and the parts to
/// include, Recipe and Costing (for the Scaling tab, its Costs). The pane's own Print button prints straight to the
/// printer picked there. The pane is made once, with the first pages, and each later set is laid out in it, so the
/// paper, orientation and parts picked stay as they were.
/// </summary>
public sealed partial class ProductPrintView : UserControl, IDisposable
{
    private readonly IDisposable _printSets;
    private ProductPrintManager? _manager;
    private PrintPreview? _preview;

    public ProductPrintView(PrintTabViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        _printSets = viewModel.WhenAnyValue(x => x.PrintSet).Where(set => set is not null).Subscribe(set => Show(set!));
    }

    private void Show(ProductPrintSet set)
    {
        if (_manager is not null)
        {
            var isAnother = _manager.PrintSet.Title != set.Title
                || (set.Title is null && _manager.PrintSet.Products[0].ProductName != set.Products[0].ProductName)
                || _manager.PrintSet.IsScaling != set.IsScaling;
            _manager.PrintSet = set;

            // Other pages start at their first, as the pane's own First button takes them, once they're laid out: the pane
            // keeps its place otherwise, which suits the same pages brought up to date, not another product or set.
            if (isAnother && _preview?.Template?.FindName("PART_PrintPreviewAreaControl", _preview) is PrintPreviewAreaControl area)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
                {
                    if (area.FirstCommand.CanExecute(null))
                    {
                        area.FirstCommand.Execute(null);
                    }
                });
            }

            return;
        }

        // The main window is themed before any of this, so the pane comes into a themed tree: the order it needs
        // (PrintPreviewThemeOrderTests). It starts on the default printer's paper, as the pane starts on that printer.
        _manager = new ProductPrintManager(set, ProductPrintManager.DefaultPrinterPaper());
        _preview = new PrintPreview(_manager);
        PrintPaneFit.Apply(_preview);
        PreviewHost.Content = _preview;
        IncludeBar.DataContext = _manager;
        _manager.PrintFailed += PrintFailed;
    }

    // The pane's Print button reports nothing when a print fails, so the tab does.
    private void PrintFailed(object? sender, Exception exception) =>
        MessageBox.Show(Window.GetWindow(this), $"The printout couldn't be sent to the printer. {exception.Message}", "Print", MessageBoxButton.OK, MessageBoxImage.Error);

    public void Dispose()
    {
        _printSets.Dispose();
        if (_manager is not null)
        {
            _manager.PrintFailed -= PrintFailed;
        }

        _preview?.Dispose();
        _manager?.Dispose();
    }
}
