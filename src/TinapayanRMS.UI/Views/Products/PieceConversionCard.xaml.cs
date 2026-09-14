using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Unit Conversion Overview: how many pieces a batch of each sub-assembly makes (its yield).
/// Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class PieceConversionCard : UserControl
{
    public PieceConversionCard()
    {
        InitializeComponent();
    }
}
