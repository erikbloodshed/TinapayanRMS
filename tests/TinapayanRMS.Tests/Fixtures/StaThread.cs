using System.Windows.Media;
using System.Windows.Threading;
using Syncfusion.Licensing;
using Syncfusion.SfSkinManager;
using TinapayanRMS.UI;

namespace TinapayanRMS.Tests.Fixtures;

/// <summary>
/// Runs a test's WPF work on an STA thread of its own, as the app's UI thread is, since WPF elements can only be made on
/// one. An exception the work throws surfaces in the test.
/// </summary>
public static class StaThread
{
    /// <summary>Runs <paramref name="test"/> on an STA thread.</summary>
    public static void Run(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
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

    /// <summary>
    /// Runs <paramref name="test"/> on an STA thread with the Syncfusion license registered, as the app has it. Skipped
    /// without SYNCFUSION_LICENSE_KEY: an unlicensed Syncfusion control shows a dialog.
    /// </summary>
    public static void RunLicensed(Action test)
    {
        var licenseKey = LicenseKey();
        Run(() =>
        {
            SyncfusionLicenseProvider.RegisterLicense(licenseKey);
            test();
        });
    }

    /// <summary>
    /// Runs <paramref name="test"/>, licensed, on the one STA thread every test that themes a window shares, with the app's
    /// theme settings (App.ThemeSettings) registered, so it's measured at the app's text sizes. SfSkinManager keeps the
    /// dictionaries it loads for a theme with the thread that loaded them, and a window themed on a second thread fails
    /// with "The calling thread cannot access this object because a different thread owns it". The tests take turns on it.
    /// </summary>
    public static void RunThemed(Action test)
    {
        var licenseKey = LicenseKey();
        ThemedThread.Value.Invoke(() =>
        {
            SyncfusionLicenseProvider.RegisterLicense(licenseKey);
            test();
        });
    }

    private static readonly Lazy<Dispatcher> ThemedThread = new(() =>
    {
        Dispatcher? dispatcher = null;
        using var started = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            SfSkinManager.RegisterThemeSettings("Windows11Light", App.ThemeSettings(Brushes.SkyBlue, Brushes.Black));
            started.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "Themed UI thread",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        started.Wait();
        return dispatcher!;
    });

    private static string LicenseKey()
    {
        var licenseKey = Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.Process)
            ?? Environment.GetEnvironmentVariable("SYNCFUSION_LICENSE_KEY", EnvironmentVariableTarget.User);
        Assert.SkipWhen(licenseKey is null, "SYNCFUSION_LICENSE_KEY isn't set, and an unlicensed Syncfusion control shows a dialog.");
        return licenseKey;
    }
}
