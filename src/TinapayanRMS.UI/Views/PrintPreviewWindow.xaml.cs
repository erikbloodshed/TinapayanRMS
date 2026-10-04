using System.Windows;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.ViewModels;

namespace TinapayanRMS.UI.Views;

/// <summary>
/// A printout previewed and printed in a dialog (2026-10-03): the Production Plan's, which isn't a product's, so it doesn't
/// go to the main window's Print tab. The window is themed before the pane goes into it: a PrintPreview already in a window
/// when SfSkinManager themes the window throws (PrintPreviewThemeOrderTests), so whoever opens this mustn't theme it again.
/// </summary>
public partial class PrintPreviewWindow : AppWindow
{
    public PrintPreviewWindow(ProductPrintSet set, Theme theme)
    {
        InitializeComponent();
        Title = set.Title ?? "Print";
        Heading.Text = $"Print: {Title}";
        SfSkinManager.SetTheme(this, theme);

        // Both go with the window: disposed when it closes.
        var manager = new ProductPrintManager(set);
        var preview = new PrintPreview(manager);
        PrintPaneFit.Apply(preview);
        PreviewHost.Content = preview;
        IncludeBar.DataContext = manager;
        manager.PrintFailed += PrintFailed;

        Closed += (_, _) =>
        {
            manager.PrintFailed -= PrintFailed;
            preview.Dispose();
            manager.Dispose();
        };
    }

    // The pane's Print button reports nothing when a print fails, so the window does.
    private void PrintFailed(object? sender, Exception exception) =>
        MessageBox.Show(this, $"The printout couldn't be sent to the printer. {exception.Message}", "Print", MessageBoxButton.OK, MessageBoxImage.Error);

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
