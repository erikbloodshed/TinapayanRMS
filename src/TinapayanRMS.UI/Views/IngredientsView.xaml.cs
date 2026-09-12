using System.Windows.Controls;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

public partial class IngredientsView : UserControl
{
    public IngredientsView(IngredientsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
