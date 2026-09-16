using System.Windows;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Helpers;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// Makes a column's cell editable on some rows and not others, which SfDataGrid's own <c>AllowEditing</c> can't do: it
/// is one setting for the whole column. Turn it on for the grid with <c>controls:GridCellEditing.IsEnabled="True"</c>,
/// then name a <see langword="bool"/> property of the row on the column, the way <c>MappingName</c> names the value:
/// <c>controls:GridCellEditing.AllowWhen="CanTypeBakersPercent"</c>. A cell whose row says false won't go into edit.
/// Columns without it are left to the grid.
/// </summary>
public static class GridCellEditing
{
    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(GridCellEditing), new PropertyMetadata(false, OnIsEnabledChanged));

    public static readonly DependencyProperty AllowWhenProperty = DependencyProperty.RegisterAttached(
        "AllowWhen", typeof(string), typeof(GridCellEditing), new PropertyMetadata(null));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    public static string? GetAllowWhen(DependencyObject element) => (string?)element.GetValue(AllowWhenProperty);

    public static void SetAllowWhen(DependencyObject element, string? value) => element.SetValue(AllowWhenProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid)
        {
            return;
        }

        grid.CurrentCellBeginEdit -= RefuseWhereNotAllowed;
        if ((bool)e.NewValue)
        {
            grid.CurrentCellBeginEdit += RefuseWhereNotAllowed;
        }
    }

    private static void RefuseWhereNotAllowed(object? sender, CurrentCellBeginEditEventArgs e)
    {
        if (sender is not SfDataGrid grid || e.Column is null || GetAllowWhen(e.Column) is not { Length: > 0 } propertyName)
        {
            return;
        }

        // The add row has no item until its edit begins; there's nothing to ask yet, so the grid decides.
        var row = grid.GetRecordAtRowIndex(e.RowColumnIndex.RowIndex);
        if (row?.GetType().GetProperty(propertyName)?.GetValue(row) is false)
        {
            e.Cancel = true;
        }
    }
}
