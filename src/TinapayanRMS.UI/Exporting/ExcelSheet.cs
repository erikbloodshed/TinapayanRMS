using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.UI.Exporting;

/// <summary>One sheet of an exported workbook: a grid as it's shown, under a name of its own.</summary>
/// <param name="Name">The sheet's tab in Excel: at most 31 characters, none of them : \ / ? * [ or ].</param>
/// <param name="Grid">The grid whose rows go on it, in its current sort and filter, with its shown columns.</param>
public sealed record ExcelSheet(string Name, SfDataGrid Grid);
