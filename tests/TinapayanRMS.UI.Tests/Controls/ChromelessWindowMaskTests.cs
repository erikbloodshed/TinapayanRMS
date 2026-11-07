using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using AwesomeAssertions;
using Syncfusion.SfSkinManager;
using Syncfusion.Windows.Shared;
using TinapayanRMS.UI.Controls;
using TinapayanRMS.UI.Tests.Fixtures;

namespace TinapayanRMS.UI.Tests.Controls;

/// <summary>
/// A themed ChromelessWindow with UseNativeChrome, as MainWindow is: the Windows11 template masks its whole content, and
/// ChromelessWindow applies the mask again whenever it updates the window region, as it does on every WM_SIZE.
/// </summary>
[NotInParallel(ChromelessWindowCollection.Name)]
public class ChromelessWindowMaskTests
{
    private const int WmSize = 0x0005;

    [Test]
    public void ChromelessWindow_masks_its_content_and_masks_it_again_on_a_resize()
    {
        // Why ChromelessWindowMask exists. If this starts failing, the template has lost its mask, and the helper can go.
        MaskAfterResize(removeMask: false).Should().NotBeNull();
    }

    [Test]
    public void Remove_takes_the_mask_off_and_a_resize_does_not_put_it_back()
    {
        MaskAfterResize(removeMask: true).Should().BeNull();
    }

    private static Brush? MaskAfterResize(bool removeMask)
    {
        Brush? mask = null;
        StaThread.RunThemed(() =>
        {
            var window = new ChromelessWindow
            {
                UseNativeChrome = true,
                CornerRadius = new CornerRadius(0),
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
                SfSkinManager.SetTheme(window, new Theme("Windows11Light"));
                window.Show();
                OffscreenGrid.Pump();

                var rootGrid = window.Template.FindName("RootGrid", window) as Grid;
                rootGrid.Should().NotBeNull("the Windows11 native-chrome template names its root RootGrid");
                if (removeMask)
                {
                    ChromelessWindowMask.Remove(window);
                }

                // What a restore or a drag sends: ChromelessWindow's hook updates the window region, and the mask with it.
                _ = SendMessage(new WindowInteropHelper(window).Handle, WmSize, IntPtr.Zero, (IntPtr)((300 << 16) | 400));
                OffscreenGrid.Pump();
                mask = rootGrid!.OpacityMask;
            }
            finally
            {
                window.Close();
            }
        });

        return mask;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
