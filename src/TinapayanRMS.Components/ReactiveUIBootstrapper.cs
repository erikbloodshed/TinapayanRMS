using ReactiveUI.Builder;

namespace TinapayanRMS.Components;

/// <summary>
/// ReactiveUI for Blazor, once per process, before anything touches a ViewModel. WithBlazor() makes the main-thread
/// scheduler run work on the calling thread: a command started from a click runs on the web view's dispatcher, which in
/// the MAUI app is the window's UI thread, and the components marshal their own re-renders through InvokeAsync.
/// </summary>
public static class ReactiveUIBootstrapper
{
    private static readonly Lazy<bool> Initialization = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithBlazor()
            .BuildApp();

        return true;
    });

    public static void EnsureInitialized() => _ = Initialization.Value;
}
