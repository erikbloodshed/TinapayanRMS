using ReactiveUI.Builder;

namespace TinapayanRMS.Presentation.Tests;

/// <summary>
/// ReactiveUI with its core services only, set up once for the whole test run before TUnit discovers a test, so before
/// any test or data source touches a ViewModel: no test sets it up itself. The ViewModels run on no UI framework here, so
/// ReactiveUI's main-thread scheduler stays its default, which runs work on the calling thread.
/// </summary>
public static class ReactiveUIBootstrapper
{
    // A hot reload rerun discovers the tests again in the same process: ReactiveUI is still set up from the first run.
    private static readonly Lazy<bool> Initialization = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithCoreServices()
            .BuildApp();

        return true;
    });

    [Before(TestDiscovery)]
    public static void Initialize() => _ = Initialization.Value;
}
