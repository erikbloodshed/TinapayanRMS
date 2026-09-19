using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' summary header: the product's title and description (edited in a pop-up from the link under
/// the title), New/Save/Delete, then its figures: material and base cost, current price and the markup it earns per
/// piece, and when it was last saved. Nothing in it is typed into but the description (ProductDetailsCard has the rest).
/// Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class ProductHeaderCard : UserControl
{
    public ProductHeaderCard()
    {
        InitializeComponent();
    }

    /// <summary>The description's pop-up opened: the cursor goes in its box, with the text selected, once the pop-up can take focus.</summary>
    private void OnDescriptionEditorOpened(object? sender, EventArgs e) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, () =>
        {
            DescriptionBox.Focus();
            DescriptionBox.SelectAll();
        });
}
