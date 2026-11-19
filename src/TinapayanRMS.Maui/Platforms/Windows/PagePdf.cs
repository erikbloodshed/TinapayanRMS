using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace TinapayanRMS.Maui.WinUI;

/// <summary>
/// The page as a PDF, through the web view's DevTools protocol: WebView2's own PrintToPdfAsync takes its paper and margins
/// from its settings, where this keeps to print.css's @page, so the file has the pages the print dialog shows.
/// </summary>
internal static class PagePdf
{
    /// <summary>As the print dialog starts out: print.css's paper and margins, no line of the browser's, no backgrounds.</summary>
    private const string Options = """{"preferCSSPageSize":true,"displayHeaderFooter":false,"printBackground":false}""";

    /// <summary>On the window's thread.</summary>
    public static async Task SaveAsync(CoreWebView2 webView, string path)
    {
        using var answer = JsonDocument.Parse(await webView.CallDevToolsProtocolMethodAsync("Page.printToPDF", Options));
        await File.WriteAllBytesAsync(path, answer.RootElement.GetProperty("data").GetBytesFromBase64());
    }
}
