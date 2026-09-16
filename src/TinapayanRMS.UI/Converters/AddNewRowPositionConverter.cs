using System.Globalization;
using System.Windows.Data;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Converters;

/// <summary>
/// An SfDataGrid's add row, from whether its lines can be typed into: <c>AddNewRowPosition.Bottom</c> when they
/// can, and <c>None</c> when they can't. <c>AllowEditing="False"</c> on its own leaves the add row there, where
/// it invites a line the grid then refuses, so a grid that shows read-only lines has to hide it as well.
/// </summary>
public sealed class AddNewRowPositionConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is true ? AddNewRowPosition.Bottom : AddNewRowPosition.None;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
