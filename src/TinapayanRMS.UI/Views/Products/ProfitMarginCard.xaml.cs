using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Profit Margin: the profit on one piece and its share of the price before VAT. Inherits the
/// Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class ProfitMarginCard : UserControl
{
    public ProfitMarginCard()
    {
        InitializeComponent();
    }
}
