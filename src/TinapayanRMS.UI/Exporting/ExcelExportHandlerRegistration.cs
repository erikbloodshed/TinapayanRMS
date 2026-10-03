using System.Windows;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Exporting;

/// <summary>
/// Answers a screen's ViewModelBase.ExportToExcel, as ConfirmDeleteHandlerRegistration answers ConfirmDelete: the View
/// asks where to save and writes its grids there, since the grids, in the sort, filter and columns the user left them,
/// are the View's. A ViewModel only asks, and says where the file went.
/// </summary>
public static class ExcelExportHandlerRegistration
{
    /// <param name="sheetsFor">The sheets to write for an export's title: a screen with several exports picks by it.</param>
    /// <returns>The registration, for a view built afresh over a longer-lived ViewModel to dispose.</returns>
    public static IDisposable RegisterExcelExport(
        this ViewModelBase viewModel, FrameworkElement view, Func<string, IReadOnlyList<ExcelSheet>> sheetsFor) =>
        viewModel.ExportToExcel.RegisterHandler(interaction =>
        {
            var title = interaction.Input;
            var path = ExcelExport.AskForPath(Window.GetWindow(view), title);
            if (path is not null)
            {
                ExcelExport.Save(path, sheetsFor(title));
            }

            interaction.SetOutput(path);
        });
}
