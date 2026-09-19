using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' details form, under the header: the product's name, category and current price, as typed in.
/// Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class ProductDetailsCard : UserControl
{
    public ProductDetailsCard()
    {
        InitializeComponent();
    }

    /// <summary>Puts the cursor in the name, so typing can start straight away.</summary>
    public void FocusName() => NameTextBox.Focus();
}
