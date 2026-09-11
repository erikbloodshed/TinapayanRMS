using System.Windows.Controls;

namespace TinapayanRMS.UI;

public partial class IngredientsView : UserControl
{
    public IngredientsView(IngredientsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += (_, _) => viewModel.LoadCommand.Execute().Subscribe();
    }
}
