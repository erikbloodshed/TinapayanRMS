using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

public partial class ProductsView : UserControl
{
    public ProductsView(ProductsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();

        // The Category picker's "< Add New .. >" row asks for the name here, after a yield that lets the picker finish
        // with the click first (RawMaterialsView explains why). The window is looked up as it's asked for.
        viewModel.PromptForNewCategory.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            interaction.SetOutput(TextPromptWindow.Prompt("New Category", interaction.Input.Label, interaction.Input.InitialValue, Window.GetWindow(this)));
        });

        // New clears the form; put the cursor in the name so typing can start straight away.
        viewModel.NewCommand.Subscribe(_ => Details.FocusName());
    }
}
