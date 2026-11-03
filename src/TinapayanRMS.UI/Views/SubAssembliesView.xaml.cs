using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

public sealed partial class SubAssembliesView : UserControl
{
    public SubAssembliesView(SubAssembliesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();

        // A name asked for by the list's menu: a sub-assembly's new name, or a role's (RawMaterialsView does the same). The
        // yield lets the menu finish with the click that ran the item first. The window is looked up as it's asked for, not
        // now: the view isn't in one yet while its constructor runs.
        viewModel.PromptForName.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            var request = interaction.Input;
            interaction.SetOutput(TextPromptWindow.Prompt(request.Title, request.Label, request.InitialValue, Window.GetWindow(this), request.Message));
        });

        // Move to In-House's name for the made ingredient, and the Made category it goes in.
        viewModel.PromptForMadeIngredient.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            interaction.SetOutput(TextPromptWindow.PromptWithChoice(interaction.Input, Window.GetWindow(this)));
        });

    }
}
