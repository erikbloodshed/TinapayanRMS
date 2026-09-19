using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>Products' Clone card: copies the saved product under a new name, or makes a variant of it that links its recipe and packaging. Inherits the Products screen's DataContext (ProductsViewModel).</summary>
public partial class CloneCard : UserControl
{
    public CloneCard()
    {
        InitializeComponent();
    }
}
