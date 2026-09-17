using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Recipe Structure: a tab per sub-assembly, one while a sub-assembly is being added and one for the
/// packaging, with the forms that add and edit them, and Add and Delete Sub-Assembly buttons. Inherits the
/// Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class RecipeStructureCard : UserControl
{
    public RecipeStructureCard()
    {
        InitializeComponent();
    }
}
