using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// The Products list: the products in a folder for their stage, then their category, and the deleted ones in the Deleted
/// folder, with a search box. Dragging products onto a folder moves them there; a right-click opens a menu of what can be
/// done with the row, or with the list; F2, Del and Enter work on the row picked (Controls/ListTree, which the Raw
/// Materials and Sub-Assemblies lists share). Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public sealed partial class ProductListCard : UserControl
{
    public ProductListCard() => InitializeComponent();
}
