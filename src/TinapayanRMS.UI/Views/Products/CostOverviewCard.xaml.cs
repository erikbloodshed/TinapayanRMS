using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>
/// Products' Recipe Cost Overview: a doughnut of how the material cost per piece splits across the
/// sub-assemblies and packaging. Inherits the Products screen's DataContext (ProductsViewModel).
/// </summary>
public partial class CostOverviewCard : UserControl
{
    public CostOverviewCard()
    {
        InitializeComponent();
    }
}
