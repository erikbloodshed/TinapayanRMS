using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' Usage Example: what a quantity of the selected material costs, converted to its base
/// unit. Its own tab, after Alternative Units. Inherits the Raw Materials screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class UsageExampleCard : UserControl
{
    public UsageExampleCard()
    {
        InitializeComponent();
    }
}
