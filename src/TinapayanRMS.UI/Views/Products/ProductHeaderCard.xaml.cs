using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' summary header: the product's name and description, its cost and price per piece, when it
/// was last saved, and New/Save/Delete. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class ProductHeaderCard : UserControl
{
    public ProductHeaderCard()
    {
        InitializeComponent();
    }

    /// <summary>Puts the cursor in the name, so typing can start straight away.</summary>
    public void FocusName() => NameTextBox.Focus();
}
