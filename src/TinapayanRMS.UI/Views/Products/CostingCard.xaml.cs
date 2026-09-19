using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Costing tab: the labor, overhead, administrative, markup and VAT fields on the left, each with what it comes
/// to in the price, beside the recipe cost breakdown on the right (materials up to the selling price, per piece, each with
/// its share of the price). Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class CostingCard : UserControl
{
    public CostingCard()
    {
        InitializeComponent();
    }
}
