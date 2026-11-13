using System.Reactive;
using ReactiveUI.Builder;

namespace TinapayanRMS.Components;

/// <summary>
/// ReactiveUI for Blazor, once per process, before anything touches a ViewModel. WithBlazor() makes the main-thread
/// scheduler run work on the calling thread: a command started from a click runs on the web view's dispatcher, which in
/// the MAUI app is the window's UI thread, and the components marshal their own re-renders through InvokeAsync.
/// <para>
/// A pipeline that fails with nothing subscribed to its ThrownExceptions (a command whose body threw outside
/// RunSafelyAsync, a ToProperty whose source errored) goes to <see cref="UnhandledFailures.Report(Exception)"/>, which
/// logs it and has the page show it. ReactiveUI's own handler threw it on the thread it failed on: the page's error bar on
/// the window's thread, and the end of the process, unlogged, on any other. The ViewModel tests keep ReactiveUI's own
/// (they set ReactiveUI up themselves), so a command that throws there still fails its test.
/// </para>
/// </summary>
public static class ReactiveUIBootstrapper
{
    private static readonly Lazy<bool> Initialization = new(() =>
    {
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithBlazor()
            .WithExceptionHandler(Observer.Create<Exception>(UnhandledFailures.Report))
            .BuildApp();

        return true;
    });

    public static void EnsureInitialized() => _ = Initialization.Value;
}
