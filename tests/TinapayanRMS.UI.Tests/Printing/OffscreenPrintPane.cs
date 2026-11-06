using System.Windows;
using System.Windows.Controls;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Printing;

/// <summary>
/// Syncfusion's print pane, fitted, over a printout in a themed window off-screen at the app's theme settings, as the main
/// window's Print tab has it. It lists the default printer's papers, as the app's does.
/// </summary>
internal static class OffscreenPrintPane
{
    /// <summary>Opens the pane on <paramref name="paper"/>, runs <paramref name="check"/> once it's laid out, and closes it.</summary>
    public static void Run(Size paper, Action<PrintPreview, ProductPrintManager> check) => StaThread.RunThemed(() =>
    {
        using var manager = new ProductPrintManager(SamplePrintouts.WithLines(10), paper);
        var host = new ContentControl();
        var window = new Window
        {
            Content = host,
            Width = 1200,
            Height = 760,
            Left = -20000,
            Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            // Themed first, then the pane, as it comes into the themed main window (PrintPreviewThemeOrderTests).
            SfSkinManager.SetTheme(window, new Theme("Windows11Light"));
            var preview = new PrintPreview(manager);
            PrintPaneFit.Apply(preview);
            host.Content = preview;

            window.Show();
            OffscreenGrid.Pump();
            check(preview, manager);
        }
        finally
        {
            window.Close();
        }
    });

    /// <summary>The pane's settings column: the printer, pages, orientation, paper and margins.</summary>
    public static PrintOptionsControl Options(PrintPreview preview) =>
        (PrintOptionsControl)preview.Template.FindName("PART_PrintOptionsControl", preview);
}
