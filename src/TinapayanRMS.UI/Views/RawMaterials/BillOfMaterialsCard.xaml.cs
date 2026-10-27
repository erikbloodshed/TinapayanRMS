using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Recipe card for a made ingredient: the sub-assembly it's made from, that recipe's lines (shown only), and its
/// own add-ins, with a row to add one.
/// Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class BillOfMaterialsCard : UserControl
{
    public BillOfMaterialsCard()
    {
        InitializeComponent();
    }
}
