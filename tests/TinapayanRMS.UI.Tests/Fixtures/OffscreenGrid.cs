using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Syncfusion.UI.Xaml.Grid;
using TinapayanRMS.UI.Controls;

namespace TinapayanRMS.UI.Tests.Fixtures;

/// <summary>
/// A real SfDataGrid in a window placed off-screen, on an STA thread of its own, for tests of the grid behaviours in
/// UI/Controls that only go wrong in a live grid's layout and selection. Exceptions the grid throws
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

    /// <summary>The layer GridFillerRows draws <paramref name="grid"/>'s empty rows on, in its template.</summary>
    public static GridFillerLayer FillerLayer(SfDataGrid grid) =>
        Descendants(grid).OfType<GridFillerLayer>().SingleOrDefault()
        ?? throw new InvalidOperationException("The grid has no filler layer: GridFillerRows isn't on, or the grid isn't loaded.");

    private static IEnumerable<DependencyObject> Descendants(DependencyObject node)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    /// <summary>Runs the dispatcher long enough for layout and the grid's own deferred work to finish.</summary>
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
