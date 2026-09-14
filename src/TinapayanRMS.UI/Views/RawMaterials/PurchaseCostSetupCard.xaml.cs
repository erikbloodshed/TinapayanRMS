using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Purchase Cost Setup: the price of one purchase unit, and what it comes to per
/// base unit. Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class PurchaseCostSetupCard : UserControl
{
    public PurchaseCostSetupCard()
    {
        InitializeComponent();
    }
}
