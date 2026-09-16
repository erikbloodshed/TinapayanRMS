using System.Windows;
using System.Windows.Threading;
using Syncfusion.Licensing;
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
    public static void Run(Func<SfDataGrid> create, Action<SfDataGrid> test)
    {
        var licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.User);
        Assert.SkipWhen(licenseKey is null, "SYNCFUSION_LICENSE_KEY isn't set, and an unlicensed Syncfusion control shows a dialog.");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            Window? window = null;
            try
            {
                SyncfusionLicenseProvider.RegisterLicense(licenseKey);

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
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                window?.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

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
