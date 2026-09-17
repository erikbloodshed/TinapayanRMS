using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

public partial class RawMaterialsView : UserControl
{
    public RawMaterialsView(RawMaterialsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.RegisterNativeMessageBoxHandler();

        // The Category picker's "< Add New .. >" row asks for the name here. The yield lets the picker finish with
        // the click first: a prompt opened from inside the pick takes no typing, since the list it was picked from
        // still has the mouse captured, and the list is left open behind it. The window is looked up as it's asked
        // for, not now: the view isn't in one yet while its constructor runs.
        viewModel.PromptForNewCategory.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            interaction.SetOutput(TextPromptWindow.Prompt("New Category", interaction.Input.Label, interaction.Input.InitialValue, Window.GetWindow(this)));
        });

        // New clears the form; put the cursor in Name so typing can start straight away.
        viewModel.NewCommand.Subscribe(_ => SummaryHeader.FocusName());
    }
}
