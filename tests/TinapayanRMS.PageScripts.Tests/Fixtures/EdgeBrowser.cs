using Microsoft.Playwright;
using TUnit.Core.Interfaces;

namespace TinapayanRMS.PageScripts.Tests.Fixtures;

/// <summary>
/// Headless Edge, the engine WebView2 is, started once for the test run and shared (ADR 0020): Playwright drives the Edge
/// installed on the PC (its msedge channel), so no browser is downloaded. Each test opens a context and page of its own
/// (<see cref="ScriptPage"/>), so the tests run side by side.
/// </summary>
public sealed class EdgeBrowser : IAsyncInitializer, IAsyncDisposable
{
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _playwright = await Playwright.CreateAsync();
        try
        {
            Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Channel = "msedge", Headless = true });
        }
        catch (PlaywrightException ex)
        {
            throw new InvalidOperationException(
                "The page scripts' tests run in the Edge installed on this PC (ADR 0020), and Playwright couldn't start it.", ex);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();
    }
}
