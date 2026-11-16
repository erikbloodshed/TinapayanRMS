namespace TinapayanRMS.Components.Tests;

/// <summary>
/// ReactiveUI set up as the app sets it up (<see cref="ReactiveUIBootstrapper"/>: Blazor's scheduler, and
/// <see cref="UnhandledFailures"/> for a pipeline that fails with nothing to catch it), once for the whole test run
/// before TUnit discovers a test, so no test or fixture sets it up itself.
/// </summary>
public static class ReactiveUISetup
{
    [Before(TestDiscovery)]
    public static void Initialize() => ReactiveUIBootstrapper.EnsureInitialized();
}
