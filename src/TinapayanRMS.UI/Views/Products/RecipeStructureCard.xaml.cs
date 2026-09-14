using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Recipe Structure: a section per sub-assembly and one for the packaging, with the forms that
/// add and edit them. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class RecipeStructureCard : UserControl
{
    public RecipeStructureCard()
    {
        InitializeComponent();
    }
}
