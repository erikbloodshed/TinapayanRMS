using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Price History for a saved purchased material, with Delete Price. Inherits the Raw
/// Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class PriceHistoryCard : UserControl
{
    public PriceHistoryCard()
    {
        InitializeComponent();
    }
}
