using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Costing Summary: the labor, overhead, administrative, markup and VAT fields as tiles, each
/// with what it comes to in the price. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class CostingSummaryCard : UserControl
{
    public CostingSummaryCard()
    {
        InitializeComponent();
    }
}
