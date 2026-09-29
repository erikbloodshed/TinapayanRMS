using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// A composite product's Products tab: the products it holds and how many of each. Inherits the Products screen's
/// DataContext (ProductsViewModel) in RecipeStructureCard, which shows it under the Products tab.
/// </summary>
public partial class ComponentsSectionView : UserControl
{
    public ComponentsSectionView()
    {
        InitializeComponent();
    }
}
