namespace TinapayanRMS.Components.Tests;

/// <summary>
/// A command run nothing awaited that fails is reported as the app reports it (<see cref="UnhandledFailures.ReportCommandFailures"/>:
/// logged, and told to the page), once for the whole test run before TUnit discovers a test, so no test or fixture sets it
/// up itself.
/// </summary>
public static class CommandFailuresSetup
{
    [Before(TestDiscovery)]
    public static void Initialize() => UnhandledFailures.ReportCommandFailures();
}
