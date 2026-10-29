using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Syncfusion.UI.Xaml.Grid;
using Syncfusion.UI.Xaml.Grid.Cells;
using Syncfusion.Windows.Shared;

namespace TinapayanRMS.UI.Controls;

/// <summary>
/// The app's SfDataGrid number column: a <see cref="GridNumericColumn"/> that groups thousands
/// with "," and right-aligns its header over the right-aligned numbers. A grid column is a
/// Freezable, not a FrameworkElement, so it can't take an implicit Style; the defaults live in
/// the constructor instead, and any of them set in XAML still wins because XAML assigns its
/// attributes after the constructor runs. Out of the box GridNumericColumn doesn't group at all
/// (NumberGroupSizes is [0]) and centres its header.
/// </summary>
public sealed class NumericColumn : GridNumericColumn
{
    public NumericColumn()
    {
        NumberGroupSeparator = ",";
        NumberGroupSizes = [3];
        HorizontalHeaderContentAlignment = HorizontalAlignment.Right;
    }

    public static readonly DependencyProperty TypedDecimalDigitsProperty = DependencyProperty.Register(
        nameof(TypedDecimalDigits), typeof(int), typeof(NumericColumn), new PropertyMetadata(-1));

    /// <summary>
    /// How many decimals can be typed into the cell, when that's more than it shows (NumberDecimalDigits): a quantity
    /// shown as 0.2 can still be typed as 0.1667, and is kept that way. -1, the default, types as many as it shows,
    /// which is all the grid's own editor allows. Needs <c>controls:NumericEditing.IsEnabled="True"</c> on the grid.
    /// </summary>
    public int TypedDecimalDigits
    {
        get => (int)GetValue(TypedDecimalDigitsProperty);
        set => SetValue(TypedDecimalDigitsProperty, value);
    }

    protected override Freezable CreateInstanceCore() => new NumericColumn();
}

/// <summary>
/// Puts <see cref="NumericEditRenderer"/> behind an SfDataGrid's number columns, so a <see cref="NumericColumn"/> with
/// <see cref="NumericColumn.TypedDecimalDigits"/> takes more decimals than it shows. Set
/// <c>controls:NumericEditing.IsEnabled="True"</c> on the grid; other number columns edit as they did.
/// </summary>
public static class NumericEditing
{
    private const string CellType = "Numeric";

    public static readonly DependencyProperty IsEnabledProperty = DependencyProperty.RegisterAttached(
        "IsEnabled", typeof(bool), typeof(NumericEditing), new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not SfDataGrid grid || !(bool)e.NewValue)
        {
            return;
        }

        grid.CellRenderers.Remove(CellType);
        grid.CellRenderers.Add(CellType, new NumericEditRenderer());
    }
}

/// <summary>
/// The number cell renderer, letting a <see cref="NumericColumn"/> with <see cref="NumericColumn.TypedDecimalDigits"/>
/// be typed to more decimals than it shows. The grid's editor (a DoubleTextBox) takes its NumberDecimalDigits from the
/// column, and that one setting fixes both how many decimals it shows and how many can be typed. For such a column the
/// editor gets MaximumNumberDecimalDigits instead, which only caps what's typed, and MinimumNumberDecimalDigits at the
/// column's, so it opens showing at least what the cell did. The cell outside editing still shows the column's decimals.
/// </summary>
public sealed class NumericEditRenderer : GridCellNumericRenderer
{
    public override void OnInitializeEditElement(DataColumnBase dataColumn, DoubleTextBox uiElement, object dataContext)
    {
        base.OnInitializeEditElement(dataColumn, uiElement, dataContext);

        // The one editor serves every column of the grid, so one without typed decimals of its own goes back to the
        // column's binding, which the base has just set.
        if (dataColumn.GridColumn is NumericColumn { TypedDecimalDigits: >= 0 } column)
        {
            BindingOperations.ClearBinding(uiElement, DoubleTextBox.NumberDecimalDigitsProperty);
            uiElement.NumberDecimalDigits = -1;
            uiElement.MinimumNumberDecimalDigits = column.NumberDecimalDigits;
            uiElement.MaximumNumberDecimalDigits = Math.Max(column.TypedDecimalDigits, column.NumberDecimalDigits);
        }
        else
        {
            uiElement.ClearValue(DoubleTextBox.MinimumNumberDecimalDigitsProperty);
            uiElement.ClearValue(DoubleTextBox.MaximumNumberDecimalDigitsProperty);
        }
    }
}
