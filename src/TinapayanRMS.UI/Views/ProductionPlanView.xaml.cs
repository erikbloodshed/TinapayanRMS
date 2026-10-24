using System.Collections.Specialized;
using System.Reactive.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ReactiveUI;
using Syncfusion.Data;
using Syncfusion.SfSkinManager;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.Exporting;
using TinapayanRMS.UI.ViewModels;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// The Production Plan screen: the saved plans, the products to make, and the mixing sheet and purchase requisition they
/// come to.
/// </summary>
public partial class ProductionPlanView : UserControl
{
    /// <summary>The mixes open to show their ingredients, by name: what's opened again once the plan replaces their rows.</summary>
    private readonly HashSet<string> _openMixes = [];

    /// <summary>True from the moment the mixes change until the rows replaced are opened again, so a row closing with its replacement isn't the user closing it.</summary>
    private bool _mixesChanging;

    public ProductionPlanView(ProductionPlanViewModel viewModel)
    {
        // Before the grid hears of the change (its ItemsSource binding is made once the DataContext is set), so a row the
        // grid closes as it replaces it isn't taken as closed by the user.
        viewModel.Mixes.CollectionChanged += MixesChanged;

        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();

        viewModel.RegisterExcelExport(this, _ => [new ExcelSheet("Plan", PlanGrid), new ExcelSheet("Mixing Sheet", MixesGrid), new ExcelSheet("Purchase Requisition", RequisitionGrid)]);

        // The plan's printout in a print pane of its own, themed as the main window is, after the button's click is over.
        viewModel.ShowPrint.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var owner = Window.GetWindow(this);
            var window = new PrintPreviewWindow(interaction.Input, SfSkinManager.GetTheme(owner)) { Owner = owner };
            window.ShowDialog();
            interaction.SetOutput(RxVoid.Default);
        });

        // A saved plan's name for Save Plan, after the button's click is over, as the Price Simulator asks for a scenario's.
        viewModel.PromptForName.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var request = interaction.Input;
            interaction.SetOutput(TextPromptWindow.Prompt(request.Title, request.Label, request.InitialValue, Window.GetWindow(this), request.Message));
        });

        // In half batches a mix can be 2.5 batches, so the Batches column shows a decimal.
        // The view lasts as long as the app, as its ViewModel does, so the subscription isn't ended.
        viewModel.WhenAnyValue(x => x.HalfBatches)
            .Subscribe(half => ((GridNumericColumn)MixesGrid.Columns["Batches"]).NumberDecimalDigits = half ? 1 : 0);

        MixesGrid.DetailsViewExpanded += (_, e) =>
        {
            if (MixOf(e.Record) is { } mix)
            {
                _openMixes.Add(mix.Name);
            }
        };
        MixesGrid.DetailsViewCollapsed += (_, e) =>
        {
            if (!_mixesChanging && MixOf(e.Record) is { } mix)
            {
                _openMixes.Remove(mix.Name);
            }
        };
    }

    private static MixRow? MixOf(object? record) => (record as RecordEntry)?.Data as MixRow ?? record as MixRow;

    /// <summary>
    /// A merge replaces a mix's row when what's in it changes, and the grid shows the new row closed: once the changes are
    /// through, each mix the user had open is opened again.
    /// </summary>
    private void MixesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_mixesChanging)
        {
            return;
        }

        _mixesChanging = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            try
            {
                if (MixesGrid.View is null)
                {
                    return;
                }

                foreach (var mix in MixesGrid.View.Records.Select(record => record.Data).OfType<MixRow>().Where(mix => _openMixes.Contains(mix.Name)).ToList())
                {
                    var recordIndex = MixesGrid.ResolveToRecordIndex(MixesGrid.ResolveToRowIndex(mix));
                    if (recordIndex >= 0 && MixesGrid.View.Records[recordIndex] is { IsExpanded: false })
                    {
                        MixesGrid.ExpandDetailsViewAt(recordIndex);
                    }
                }
            }
            finally
            {
                _mixesChanging = false;
            }
        });
    }
}
