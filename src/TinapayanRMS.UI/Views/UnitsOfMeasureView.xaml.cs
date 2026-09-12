using System.Windows.Controls;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

public partial class UnitsOfMeasureView : UserControl
{
    public UnitsOfMeasureView(UnitsOfMeasureViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
