using System.Windows;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The app's SfDataGrid number column: a <see cref="GridNumericColumn"/> that groups thousands
/// with "," and right-aligns its header over the right-aligned numbers. A grid column is a
/// Freezable, not a FrameworkElement, so it can't take an implicit Style; the defaults live in
/// the constructor instead, and any of them set in XAML still wins because XAML assigns its
/// attributes after the constructor runs. Out of the box GridNumericColumn doesn't group at all
/// (NumberGroupSizes is [0]) and centres its header.
/// </summary>
public class NumericColumn : GridNumericColumn
{
    public NumericColumn()
    {
        NumberGroupSeparator = ",";
        NumberGroupSizes = [3];
        HorizontalHeaderContentAlignment = HorizontalAlignment.Right;
    }

    protected override Freezable CreateInstanceCore() => new NumericColumn();
}
