using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' list: ingredients and packaging together, with a search box. Inherits the Raw
/// Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class MaterialListCard : UserControl
{
    public MaterialListCard()
    {
        InitializeComponent();
    }
}
