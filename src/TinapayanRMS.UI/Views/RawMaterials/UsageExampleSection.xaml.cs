using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Usage Example: what a quantity of the selected material costs, converted to its base
/// unit. A section, not a card: it sits at the bottom of whichever cost card shows (Purchase Cost Setup,
/// Bill of Materials or Linked Cost). Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class UsageExampleSection : UserControl
{
    public UsageExampleSection()
    {
        InitializeComponent();
    }
}
