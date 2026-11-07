using System.Collections;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.Pricing;
using TinapayanRMS.Presentation.ViewModels;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The unit list a packaging grid's Unit cell offers, taken from the row rather than the screen: every row has its
/// own material, so every row has its own base unit and alternative units
/// (<see cref="PackagingLineRow.UnitOptions"/>). A <see cref="GridComboBoxColumn"/> binds one collection for the
/// whole column, so a per-row list has to come through <see cref="IItemsSourceSelector"/>
/// (ItemsSourceSelector="{StaticResource PackagingUnitSelector}").
/// </summary>
public sealed class PackagingUnitSelector : IItemsSourceSelector
{
    /// <summary>
    /// Called per cell: <paramref name="record"/> is the row, and is null or some other type while the grid is
    /// between rows, which gets an empty list rather than the previous row's units.
    /// </summary>
    public IEnumerable GetItemsSource(object record, object dataContext) =>
        record is PackagingLineRow row ? row.UnitOptions : Array.Empty<PackagingUnitChoice>();
}
