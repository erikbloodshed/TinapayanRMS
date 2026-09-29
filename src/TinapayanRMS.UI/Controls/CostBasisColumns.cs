using System.Windows;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.Domain.Enums;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Shows the cost column of a material picker's pop-up at the Products screen's cost basis. A picker cell
/// (GridMultiColumnDropDownList) and the columns of its pop-up aren't in the visual tree, so they can't bind to the
/// screen; the grid holding them can. Each pop-up has two cost columns, one per basis, marked
/// <c>controls:CostBasisColumns.Shows="Buffered"</c> or <c>"Unbuffered"</c> (that one starting <c>IsHidden="True"</c>),
/// and the grid binds <c>controls:CostBasisColumns.Basis="{Binding CostBasis}"</c>: the marked column that doesn't
/// match is hidden. The pop-up is built each time a cell is edited, so it opens with the columns as they are then.
/// </summary>
public static class CostBasisColumns
{
    /// <summary>On a pop-up's column: the cost it shows. Columns without it are left alone.</summary>
    public static readonly DependencyProperty ShowsProperty = DependencyProperty.RegisterAttached(
        "Shows", typeof(CostBasis?), typeof(CostBasisColumns), new PropertyMetadata(null));

    public static CostBasis? GetShows(DependencyObject column) => (CostBasis?)column.GetValue(ShowsProperty);

    public static void SetShows(DependencyObject column, CostBasis? value) => column.SetValue(ShowsProperty, value);

    /// <summary>On the SfDataGrid: which cost its pickers' pop-ups show.</summary>
    public static readonly DependencyProperty BasisProperty = DependencyProperty.RegisterAttached(
        "Basis", typeof(CostBasis), typeof(CostBasisColumns), new PropertyMetadata(CostBasis.Buffered, OnBasisChanged));

    public static CostBasis GetBasis(DependencyObject grid) => (CostBasis)grid.GetValue(BasisProperty);

    public static void SetBasis(DependencyObject grid, CostBasis value) => grid.SetValue(BasisProperty, value);

    private static void OnBasisChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        var basis = (CostBasis)e.NewValue;
        foreach (var picker in grid.Columns.OfType<GridMultiColumnDropDownList>())
        {
            foreach (var column in picker.Columns)
            {
                if (GetShows(column) is { } shows)
                {
                    column.IsHidden = shows != basis;
                }
            }
        }
    }
}
