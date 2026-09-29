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

        // A name typed into the Category picker that isn't a category yet: asks whether to add it, after a yield that
        // lets the picker finish with the key or click first (RawMaterialsView explains why).
        viewModel.PromptForNewCategory.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            interaction.SetOutput(TextPromptWindow.Prompt(
                "New Category", "Name", interaction.Input, Window.GetWindow(this), RawMaterialsView.NewCategoryMessage(interaction.Input)));
        });

        // A name asked for by the list's menu: a product's new name, a clone's or a variant's, or a category's.
        viewModel.PromptForName.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var request = interaction.Input;
            interaction.SetOutput(TextPromptWindow.Prompt(request.Title, request.Label, request.InitialValue, Window.GetWindow(this), request.Message));
        });

        // New clears the form, as New Product Here does, and their composite ones; put the cursor in the name so typing can
        // start straight away.
        viewModel.NewCommand.Subscribe(_ => Details.FocusName());
        viewModel.NewProductInCommand.Subscribe(_ => Details.FocusName());
        viewModel.NewCompositeCommand.Subscribe(_ => Details.FocusName());
        viewModel.NewCompositeProductInCommand.Subscribe(_ => Details.FocusName());
    }
}
