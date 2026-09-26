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

        // A name typed into the Category picker that isn't a category yet: asks whether to add it, starting with the
        // name so it needn't be typed again. The yield lets the picker finish with the key or click that left it first:
        // a prompt opened while something still has the mouse captured takes no typing. The window is looked up as
        // it's asked for, not now: the view isn't in one yet while its constructor runs.
        viewModel.PromptForNewCategory.RegisterHandler(async interaction =>
        {
            await Dispatcher.Yield(DispatcherPriority.Background);
            interaction.SetOutput(TextPromptWindow.Prompt(
                "New Category", "Name", interaction.Input, Window.GetWindow(this), NewCategoryMessage(interaction.Input)));
        });

        // New clears the form; put the cursor in Name so typing can start straight away.
        viewModel.NewCommand.Subscribe(_ => SummaryHeader.FocusName());
    }

    /// <summary>The prompt's line for a name typed into a Category picker that isn't a category yet (Products uses it too).</summary>
    internal static string NewCategoryMessage(string name) => $"“{name}” isn't a category yet. Add it?";
}
