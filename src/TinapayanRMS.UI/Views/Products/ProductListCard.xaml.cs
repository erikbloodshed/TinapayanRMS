using System.ComponentModel;
using System.Windows.Controls;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.ScrollAxis;

namespace TinapayanRMS.UI.Views.Products;

/// <summary>The Products list, with a search box. Inherits the Products screen's DataContext (ProductsViewModel).</summary>
public partial class ProductListCard : UserControl
{
    public ProductListCard()
    {
        InitializeComponent();

        // A product picked from elsewhere (Sub-Assemblies' Used By tiles) may be scrolled out of sight: the grid
        // doesn't bring a SelectedItem set through its binding into view itself. A row clicked is already in view,
        // so this does nothing for it.
        DependencyPropertyDescriptor.FromProperty(SfDataGrid.SelectedItemProperty, typeof(SfDataGrid))
            .AddValueChanged(List, (_, _) =>
            {
                if (List.SelectedItem is { } product && List.ResolveToRowIndex(product) is var rowIndex and >= 0)
                {
                    List.ScrollInView(new RowColumnIndex(rowIndex, 0));
                }
            });
    }
}
