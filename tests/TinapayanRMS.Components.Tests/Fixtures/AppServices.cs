using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Syncfusion.Blazor;
using TinapayanRMS.ApplicationCore.Interfaces;
using TinapayanRMS.Components.Shared;

namespace TinapayanRMS.Components.Tests.Fixtures;

/// <summary>
/// The app's own registrations (ServiceRegistration.AddTinapayanRMS) in a bUnit context, which stands in for what the
/// web view and the MAUI app give a page: navigation, JavaScript, and the window (<see cref="FakeAppWindow"/>). Nothing
/// here opens a connection; the connection string only has to be one.
/// </summary>
public static class AppServices
{
    public const string ConnectionString = "Host=localhost;Database=tinapayanrms_test_components;Username=tinapayanrms";

    public static BunitContext NewContext()
    {
        ReactiveUIBootstrapper.EnsureInitialized();

        var context = new BunitContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = ConnectionString })
            .Build();
        context.Services.AddSyncfusionBlazor();
        // A file that's never read or written: the store below takes its place.
        context.Services.AddTinapayanRMS(configuration, Path.Combine(Path.GetTempPath(), "tinapayanrms-components-tests", "view-state.json"));

        // The Windows user's view state is a file in their app data folder: the tests keep theirs in memory.
        context.Services.AddSingleton<IViewStateStore>(new InMemoryViewStateStore());
        context.Services.AddSingleton<FakeAppWindow>();
        context.Services.AddSingleton<IAppWindow>(provider => provider.GetRequiredService<FakeAppWindow>());
        return context;
    }

    /// <summary>
    /// Renders as the app's BlazorWebView does, interactively, for a test that draws a Syncfusion dialog: the dialog asks,
    /// to tell a static page from an interactive one. Once the test's services are all added, since asking for the
    /// renderer builds them, and none can be added after.
    /// </summary>
    public static BunitContext RenderingAsTheWebView(this BunitContext context)
    {
        context.Renderer.SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("WebView", isInteractive: true));
        return context;
    }
}
