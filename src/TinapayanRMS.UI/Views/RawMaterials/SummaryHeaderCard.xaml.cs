using System.Windows.Controls;

namespace TinapayanRMS.UI.Views.RawMaterials;

/// <summary>
/// Raw Materials' summary header: what the material is, its base unit and cost per base unit,
/// New/Save/Delete, and its type, category, source and weight loss. Inherits the Raw Materials
/// screen's DataContext (RawMaterialsViewModel).
/// </summary>
public partial class SummaryHeaderCard : UserControl
{
    public SummaryHeaderCard()
    {
        InitializeComponent();
    }

    /// <summary>Puts the cursor in Name, so typing can start straight away.</summary>
    public void FocusName() => NameTextBox.Focus();
}
