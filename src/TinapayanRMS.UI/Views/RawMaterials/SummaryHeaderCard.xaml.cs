using System.Windows;
using System.Windows.Controls;
using TinapayanRMS.UI.ViewModels;

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

    /// <summary>Checks the name against the other materials as soon as the field is left, not just on Save.</summary>
    private void NameTextBox_LostFocus(object sender, RoutedEventArgs e) => ((RawMaterialsViewModel)DataContext).CheckNameAvailability();
}
