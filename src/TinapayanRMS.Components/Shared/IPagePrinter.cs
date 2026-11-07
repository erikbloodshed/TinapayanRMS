using Microsoft.JSInterop;

namespace TinapayanRMS.Components.Shared;

/// <summary>
/// Prints the window's page, which print.css lays out as the printout alone, through the computer's print dialog.
/// </summary>
public interface IPagePrinter
{
    Task PrintAsync();
}

/// <summary>
/// The web view's own print dialog (window.print), which Windows' WebView2 has. A Mac's web view has none, so the MAUI
/// app puts its own printer in this one's place there.
/// </summary>
public sealed class WebViewPagePrinter(IJSRuntime js) : IPagePrinter
{
    public async Task PrintAsync() => await js.InvokeVoidAsync("tinapayan.print");
}
