using System.Windows;
using System.Windows.Interop;
using AwesomeAssertions;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>A window drawn in software from being minimized until it has settled after a restore, and with the GPU otherwise.</summary>
public class RestoreRenderingTests
{
    [Test]
    public void A_window_draws_in_software_while_minimized_and_just_after_a_restore_then_with_the_GPU() => StaThread.Run(() =>
    {
        var window = new Window
        {
            Width = 400,
            Height = 300,
            Left = -20000,
            Top = -20000,
            WindowStartupLocation = WindowStartupLocation.Manual,
            ShowActivated = false,
            ShowInTaskbar = false,
        };
        try
        {
            window.Show();
            OffscreenGrid.Pump();
            RestoreRendering.UseSoftwareWhileRestoring(window);
            var target = ((HwndSource)PresentationSource.FromVisual(window)).CompositionTarget;
            target.RenderMode.Should().Be(RenderMode.Default, "a window that hasn't been minimized draws with the GPU");

            window.WindowState = WindowState.Minimized;
            target.RenderMode.Should().Be(RenderMode.SoftwareOnly);

            window.WindowState = WindowState.Normal;
            target.RenderMode.Should().Be(RenderMode.SoftwareOnly, "the restore itself is drawn in software");

            // Pump returns after 500 ms, SoftwareAfterRestore's length; the second covers the timer's tick.
            OffscreenGrid.Pump();
            OffscreenGrid.Pump();
            target.RenderMode.Should().Be(RenderMode.Default);
        }
        finally
        {
            window.Close();
        }
    });
}
