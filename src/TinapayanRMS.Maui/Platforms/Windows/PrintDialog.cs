using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// Cancels the web view's print dialog (window.print). WebView2 has no call for it, and the page can't do it, since the
/// dialog holds the page's script still until it closes. The dialog is a page of the browser's own, though, which the web
/// view's DevTools protocol can find and press Escape in, as the user would to cancel it.
/// </summary>
internal static class PrintDialog
{
    /// <summary>How many times it looks for the dialog, so one still opening as the window locks goes too.</summary>
    private const int Looks = 6;

    private static readonly TimeSpan BetweenLooks = TimeSpan.FromMilliseconds(500);

    /// <summary>How long a key press waits for its answer: the dialog can go before it gives one.</summary>
    private static readonly TimeSpan KeyAnswer = TimeSpan.FromSeconds(1);

    private const string EscapeKeyDown =
        """{"type":"rawKeyDown","key":"Escape","code":"Escape","windowsVirtualKeyCode":27,"nativeVirtualKeyCode":27}""";

    /// <summary>On the window's thread. Nothing awaits it, so it logs its own failure.</summary>
    public static async Task CloseAsync(CoreWebView2 webView)
    {
        try
        {
            for (var look = 0; look < Looks; look++)
            {
                if (look > 0)
                {
                    await Task.Delay(BetweenLooks);
                }

                if (await FindAsync(webView) is { } dialog)
                {
                    await PressEscapeAsync(webView, dialog);
                }
            }

            if (await FindAsync(webView) is not null)
            {
                Log.Error("The print dialog stayed open after the window locked");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Couldn't close the print dialog as the window locked");
        }
    }

    /// <summary>The dialog's target, while it's open: its page's address is the browser's print page.</summary>
    private static async Task<string?> FindAsync(CoreWebView2 webView)
    {
        using var targets = JsonDocument.Parse(await webView.CallDevToolsProtocolMethodAsync("Target.getTargets", "{}"));
        foreach (var target in targets.RootElement.GetProperty("targetInfos").EnumerateArray())
        {
            if (target.GetProperty("type").GetString() == "page"
                && target.GetProperty("url").GetString()?.Contains("://print/", StringComparison.Ordinal) == true)
            {
                return target.GetProperty("targetId").GetString();
            }
        }

        return null;
    }

    private static async Task PressEscapeAsync(CoreWebView2 webView, string dialog)
    {
        var attach = JsonSerializer.Serialize(new { targetId = dialog, flatten = true });
        using var attached = JsonDocument.Parse(await webView.CallDevToolsProtocolMethodAsync("Target.attachToTarget", attach));
        var session = attached.RootElement.GetProperty("sessionId").GetString();
        try
        {
            await webView.CallDevToolsProtocolMethodForSessionAsync(session, "Input.dispatchKeyEvent", EscapeKeyDown)
                .AsTask().WaitAsync(KeyAnswer);
        }
        catch (TimeoutException)
        {
            // The dialog went before it answered. The next look says whether it did.
        }
    }
}
