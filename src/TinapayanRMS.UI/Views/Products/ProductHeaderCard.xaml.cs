using System.Windows;
using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' summary header: the product's title, New/Save/Delete, then its figures: material and base cost,
/// current price and the markup it earns per piece, and when it was last saved. Nothing in it is typed into
/// (ProductDetailsCard has the fields). Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class ProductHeaderCard : UserControl
{
    public ProductHeaderCard()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Shuts New's drop-down after one of its items was clicked, as Add Sub-Assembly's does: a SplitButtonAdv closes it
    /// only from its own mouse-up, which the ButtonAdv clicked has already handled. The item's Command runs the rest.
    /// </summary>
    private void NewItem_Click(object sender, RoutedEventArgs e) => NewButton.IsDropDownOpen = false;
}
