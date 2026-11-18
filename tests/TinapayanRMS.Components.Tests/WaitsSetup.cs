namespace TinapayanRMS.Components.Tests;

/// <summary>
/// How long a component test waits for what it expects, once for the whole test run. bUnit's own default is one second,
/// which the screens' work can overrun with the whole suite running beside it. A wait that's met ends at once, so only a
/// test that's failing takes this long.
/// </summary>
public static class WaitsSetup
{
    [Before(TestDiscovery)]
    public static void Initialize() => BunitContext.DefaultWaitTimeout = TimeSpan.FromSeconds(30);
}
