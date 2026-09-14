using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Recipe Usage Example: the material cost of a number of pieces, broken down by sub-assembly
/// and packaging. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class RecipeUsageExampleCard : UserControl
{
    public RecipeUsageExampleCard()
    {
        InitializeComponent();
    }
}
