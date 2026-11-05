using System.Windows;
using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' list: the materials in a folder for their type, then their category, with a search box. It works as the
/// Products list does (Controls/ListTree): dragging materials onto another category of their type moves them there; a
/// right-click opens a menu of what can be done with the row, or with the list; F2, Del and Enter work on the row picked.
/// Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public sealed partial class MaterialListCard : UserControl
{
    public MaterialListCard() => InitializeComponent();

    /// <summary>Closes New Category's drop-down once an item in it is clicked: a DropDownButtonAdv doesn't on its own.</summary>
    private void NewCategoryItem_Click(object sender, RoutedEventArgs e) => NewCategoryButton.IsDropDownOpen = false;
}
