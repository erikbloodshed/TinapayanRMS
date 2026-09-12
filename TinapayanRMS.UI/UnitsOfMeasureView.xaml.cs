using System.Windows.Controls;

namespace TinapayanRMS.UI;

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
