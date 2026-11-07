using System.Reactive.Linq;
using System.Windows.Controls;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>The Settings dialog's Product Categories tab. SettingsWindow registers its delete confirmation.</summary>
public sealed partial class ProductCategoriesView : UserControl
{
    public ProductCategoriesView(ProductCategoriesViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        // New, and a finished Save, clear the form: put the cursor in Name for the next category. Only
        // while shown, since the ViewModel outlives this view (Settings builds a new one each time).
        IDisposable? focusName = null;
        Loaded += (_, _) => focusName ??= viewModel.NewCommand.Merge(viewModel.SaveCommand).Subscribe(_ => NameTextBox.Focus());
        Unloaded += (_, _) =>
        {
            focusName?.Dispose();
            focusName = null;
        };
    }
}
