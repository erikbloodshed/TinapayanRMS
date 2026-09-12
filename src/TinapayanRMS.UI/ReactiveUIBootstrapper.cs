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
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .WithWpf()
            .BuildApp();

        return true;
    });

    public static void EnsureInitialized() => _ = Initialization.Value;
}
