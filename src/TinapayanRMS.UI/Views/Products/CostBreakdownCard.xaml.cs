using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Recipe Cost Breakdown: materials, labor, overhead, administrative, base cost, markup and VAT
/// per piece, each with its share of the selling price. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class CostBreakdownCard : UserControl
{
    public CostBreakdownCard()
    {
        InitializeComponent();
    }
}
