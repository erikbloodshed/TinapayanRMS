using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Unit Conversion Overview: how each alternative unit converts to the base unit.
/// Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public sealed partial class UnitConversionCard : UserControl
{
    public UnitConversionCard()
    {
        InitializeComponent();
    }
}
