using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Usage Example: what a quantity of any material costs, converted to its base unit.
/// Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class UsageExampleCard : UserControl
{
    public UsageExampleCard()
    {
        InitializeComponent();
    }
}
