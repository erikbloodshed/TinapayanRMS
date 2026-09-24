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
}
