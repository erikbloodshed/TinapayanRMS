using System.Windows;
using System.Windows.Threading;
using ReactiveUI.Builder;

namespace TinapayanRMS.UI;

/// <summary>
/// ReactiveUI 24+ requires explicit builder-pattern initialization before any
/// ReactiveObject is touched. This must run exactly once per process, so both
/// App.xaml.cs (production) and the Tests project (which builds the host
/// directly, without going through App) call EnsureInitialized() rather than
/// initializing ReactiveUI themselves.
/// </summary>
public static class ReactiveUIBootstrapper
{
    private static readonly Lazy<bool> Initialization = new(() =>
    {
        // WithWpf() binds the main-thread scheduler to the Application's dispatcher,
        // or, when there is no Application (tests), to the calling thread's existing
        // dispatcher, and throws if that thread has none. Since ReactiveUI 24.3 it no
        // longer creates one, so create it here as 24.2 did.
        if (Application.Current is null)
            _ = Dispatcher.CurrentDispatcher;

        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .WithWpf()
            .BuildApp();

        return true;
    });

    public static void EnsureInitialized() => _ = Initialization.Value;
}
