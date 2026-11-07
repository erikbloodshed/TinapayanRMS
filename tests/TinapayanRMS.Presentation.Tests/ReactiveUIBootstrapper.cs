using ReactiveUI.Builder;

namespace TinapayanRMS.Presentation.Tests;

/// <summary>
/// ReactiveUI with its core services only, once per test run, before a test touches a ViewModel. The ViewModels run
/// on no UI framework here, so ReactiveUI's main-thread scheduler stays its default, which runs work on the calling thread.
/// </summary>
public static class ReactiveUIBootstrapper
{
    private static readonly Lazy<bool> Initialization = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();

        return true;
    });

    public static void EnsureInitialized() => _ = Initialization.Value;
}
