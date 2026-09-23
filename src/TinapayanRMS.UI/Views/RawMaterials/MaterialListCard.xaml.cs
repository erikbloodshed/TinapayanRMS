using System.ComponentModel;
using System.Windows.Controls;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.ScrollAxis;

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

        // A material picked from elsewhere (the main window's Back and Forward) may be scrolled out of sight, as in
        // ProductListCard: the grid doesn't bring a SelectedItem set through its binding into view itself.
        DependencyPropertyDescriptor.FromProperty(SfDataGrid.SelectedItemProperty, typeof(SfDataGrid))
            .AddValueChanged(List, (_, _) =>
            {
                if (List.SelectedItem is { } material && List.ResolveToRowIndex(material) is var rowIndex and >= 0)
                {
                    List.ScrollInView(new RowColumnIndex(rowIndex, 0));
                }
            });
    }
}
