using System.Windows;
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

    /// <summary>
    /// Shuts Add Sub-Assembly's drop-down after one of its three ways in was clicked. A DropDownButtonAdv doesn't do
    /// it itself: it closes its drop-down from its own OnMouseLeftButtonUp, which a click inside the drop-down never
    /// reaches — the ButtonAdv clicked has already handled it. The command runs from the item's own Command binding.
    /// </summary>
    private void AddSubAssemblyItem_Click(object sender, RoutedEventArgs e) => AddSubAssemblyButton.IsDropDownOpen = false;
}
