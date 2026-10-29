using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Used By: the products the material goes into, each a tile that opens it on the Products screen.
/// Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public sealed partial class UsedByCard : UserControl
{
    public UsedByCard()
    {
        InitializeComponent();
    }
}
