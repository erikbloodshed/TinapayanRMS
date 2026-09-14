using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Bill of Materials for a made ingredient: its lines, and a row to add one.
/// Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class BillOfMaterialsCard : UserControl
{
    public BillOfMaterialsCard()
    {
        InitializeComponent();
    }
}
