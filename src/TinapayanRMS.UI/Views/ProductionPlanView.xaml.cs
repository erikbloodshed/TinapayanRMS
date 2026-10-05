using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Syncfusion.SfSkinManager;
using TinapayanRMS.UI.Exporting;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Views;

/// <summary>The Production Plan screen: the products to make, and the mixing sheet and purchase requisition they come to.</summary>
public partial class ProductionPlanView : UserControl
{
    public ProductionPlanView(ProductionPlanViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();

        viewModel.RegisterExcelExport(this, _ => [new ExcelSheet("Mixing Sheet", MixesGrid), new ExcelSheet("Purchase Requisition", RequisitionGrid)]);

        // The plan's printout in a print pane of its own, themed as the main window is, after the button's click is over.
        viewModel.ShowPrint.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var owner = Window.GetWindow(this);
            var window = new PrintPreviewWindow(interaction.Input, SfSkinManager.GetTheme(owner)) { Owner = owner };
            window.ShowDialog();
            interaction.SetOutput(RxVoid.Default);
        });
    }
}
