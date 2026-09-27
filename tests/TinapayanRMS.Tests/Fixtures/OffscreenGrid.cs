using System.Windows;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;

namespace TinapayanRMS.Tests.Fixtures;

/// <summary>
/// A real SfDataGrid in a window placed off-screen, on an STA thread of its own, for tests of the grid behaviours in
/// UI/Controls that only go wrong in a live grid's layout and selection (GridFillerRows). Exceptions the grid throws
/// from a dispatcher callback surface from <see cref="Pump"/>, as they'd have closed the app.
/// </summary>
public static class OffscreenGrid
{
    /// <summary>
    /// Makes the grid with <paramref name="create"/>, shows it and lets it settle, then hands it to <paramref name="test"/>,
    /// all on one STA thread. Skipped without SYNCFUSION_LICENSE_KEY: an unlicensed Syncfusion control shows a dialog.
    /// </summary>
    public static void Run(Func<SfDataGrid> create, Action<SfDataGrid> test) =>
        StaThread.RunLicensed(() =>
        {
            Window? window = null;
            try
            {
                var grid = create();
                grid.VerticalAlignment = VerticalAlignment.Top;
                window = new Window
                {
                    Content = grid,
                    Width = 900,
                    Height = 900,
                    Left = -20000,
                    Top = -20000,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                };
                window.Show();
                Pump();

                test(grid);
            }
            finally
            {
                window?.Close();
            }
        });

    /// <summary>Runs the dispatcher long enough for layout and GridFillerRows' settle delay (150 ms) to finish.</summary>
    public static void Pump()
    {
        var until = DateTime.UtcNow.AddMilliseconds(500);
        while (DateTime.UtcNow < until)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => frame.Continue = false);
            Dispatcher.PushFrame(frame);
            Thread.Sleep(15);
        }
    }
}
