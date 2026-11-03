using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Scaling tab: how many sellable units to make, and the recipe scaled to them in a tree (sections in whole
/// batches with their lines, made ingredients opening into their BOMs, a composite's products, the packaging), each row
/// with its amount and cost. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public sealed partial class ScalingCard : UserControl
{
    public ScalingCard()
    {
        InitializeComponent();
    }
}
