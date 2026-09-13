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

        // New clears the form; put the cursor in Name so typing can start straight away.
        viewModel.NewCommand.Subscribe(_ => NameTextBox.Focus());
    }
}
