using System.Windows;
using System.Windows.Controls;
using AwesomeAssertions;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared.Printing;
using TinapayanRMS.UI.Printing;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Printing;

/// <summary>
/// Syncfusion's print pane (PrintPreview, 34.2.9) cares when its window is themed: a pane already in the window when
/// SfSkinManager.SetTheme reaches it throws from OnApplyTemplate on its first layout, which is what crashed the app the
/// first time Print was pressed, when it had a window of its own. The Print tab's pane comes into the main window,
/// themed at startup, so it gets the right order, as SfDiagram's own print window does. The second half fails once
/// Syncfusion fixes the pane, when that order stops mattering.
/// </summary>
public class PrintPreviewThemeOrderTests
{
    /// <summary>
    /// On the one thread every test that themes a window shares (StaThread.RunThemed), as the app has one UI thread:
    /// SfSkinManager keeps the theme's dictionaries it loads for the thread that loaded them, and a window themed on
    /// another thread then can't read them.
    /// </summary>
    [Fact]
    public void A_pane_opens_in_a_window_themed_before_it_comes_in_and_throws_in_one_themed_after() => StaThread.RunThemed(() =>
    {
        using (var manager = new ProductPrintManager(SamplePrintouts.WithLines(10), ProductPrintManager.A4))
        {
            var host = new ContentControl();
            var window = OffscreenWindow(host);
            try
            {
                SfSkinManager.SetTheme(window, new Theme("Windows11Light"));
                host.Content = new PrintPreview(manager);

                window.Show();
                OffscreenGrid.Pump();

                manager.PageCount.Should().Be(1, "the pane laid the printout out");
            }
            finally
            {
                window.Close();
            }
        }

        using (var manager = new ProductPrintManager(SamplePrintouts.WithLines(10), ProductPrintManager.A4))
        {
            var window = OffscreenWindow(new ContentControl { Content = new PrintPreview(manager) });
            try
            {
                SfSkinManager.SetTheme(window, new Theme("Windows11Light"));

                window.Invoking(w => w.Show()).Should().Throw<NullReferenceException>()
                    .Which.StackTrace.Should().Contain("PrintPreview.OnApplyTemplate");
            }
            finally
            {
                window.Close();
            }
        }
    });

    private static Window OffscreenWindow(ContentControl content) => new()
    {
        Content = content,
        Width = 1100,
        Height = 760,
        Left = -20000,
        Top = -20000,
        WindowStartupLocation = WindowStartupLocation.Manual,
        ShowActivated = false,
        ShowInTaskbar = false,
    };
}
